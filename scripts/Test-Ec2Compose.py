"""Exercise the EC2 Compose definition locally, using real API images and disposable PostgreSQL."""
import argparse
import json
import os
from pathlib import Path
import secrets
import shutil
import subprocess
import sys
import tempfile
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]
DEPLOY = ROOT / "infra/aws/dev"
SERVICES = ("academic", "assistant", "notifications")
POSTGRES = "postgres:18-alpine@sha256:10a5c69a83035ab5239f5620d708520e8b28f5aa1f2b20143e6024532edadfb5"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assistant-source", type=Path, default=ROOT.parent / "nomi-assistant")
    parser.add_argument("--notifications-source", type=Path, default=ROOT.parent / "nomi-notifications")
    args = parser.parse_args()
    docker = shutil.which("docker")
    if not docker:
        raise SystemExit("Docker não encontrado.")
    prefix = "nomi-compose-check-" + uuid.uuid4().hex[:10]
    network = prefix + "-db"
    pg = prefix + "-postgres"
    resources = []
    images = []
    network_created = False
    password = "Nomi-" + secrets.token_hex(24) + "$literal"
    env = {key: value for key, value in os.environ.items() if not key.startswith(("NOMI_", "COMPOSE_"))}

    def run(argv, *, input=None, check=True, timeout=180):
        result = subprocess.run(argv, input=input, text=True, encoding="utf-8", errors="replace",
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env, timeout=timeout)
        if check and result.returncode:
            message = (result.stderr + result.stdout).replace(password, "[REDACTED]")
            raise RuntimeError(message[-5000:])
        return result

    def dc(*args, **kwargs):
        return run([docker, *args], **kwargs)

    with tempfile.TemporaryDirectory(prefix=prefix) as directory:
        directory = Path(directory)
        configs = directory / "env"
        configs.mkdir(mode=0o700)
        configs.chmod(0o700)
        ca = directory / "global-bundle.pem"
        ca.write_text("Public certificate fixture; integration uses local PostgreSQL without TLS.\n")
        ca.chmod(0o644)
        runtime = directory / "runtime.env"
        runtime.write_text(f"NOMI_SECRETS_DIR={configs.as_posix()}\nNOMI_RDS_CA={ca.as_posix()}\n", encoding="utf-8")
        release = directory / "release.env"
        release.write_text((DEPLOY / "release.env").read_text(encoding="utf-8"), encoding="utf-8")
        for service in SERVICES:
            connection = (f"Host={pg};Database=nomi_{service};Username=nomi_{service}_app;"
                          f"Password={password};Timeout=2;Command Timeout=3")
            path = configs / (service + ".env")
            path.write_text(f"ConnectionStrings__{service.capitalize()}={connection}\n", encoding="utf-8")
            path.chmod(0o600)
        # Validate strict production image constraints and shell-variable isolation without connecting to AWS.
        helper = [sys.executable, str(DEPLOY / "compose.py"), "--release", str(release), "--runtime", str(runtime)]
        result = run(helper + ["check"])
        assert password not in result.stdout + result.stderr
        original = release.read_text(encoding="utf-8")
        first_key = original.splitlines()[2].split("=", 1)[0]
        for bad in [original.replace("@sha256:", ":latest#", 1), original + f"\n{first_key}=duplicate\n"]:
            release.write_text(bad, encoding="utf-8")
            rejected = run(helper + ["check"], check=False)
            assert rejected.returncode != 0 and password not in rejected.stdout + rejected.stderr
        release.write_text(original, encoding="utf-8")
        assistant_env = configs / "assistant.env"
        original_env = assistant_env.read_text(encoding="utf-8")
        assistant_env.write_text(original_env.replace("ConnectionStrings__Assistant", "ConnectionStrings__Notifications"), encoding="utf-8")
        assert run(helper + ["check"], check=False).returncode != 0
        assistant_env.write_text(original_env, encoding="utf-8")
        print("Pré-validação: digests obrigatórios, chaves duplicadas/trocadas rejeitadas e senhas não exibidas.", flush=True)

        override = directory / "override.json"
        definition = {"services": {}, "networks": {"testdb": {"external": True, "name": network}}}
        for service in SERVICES:
            definition["services"][service] = {
                "container_name": prefix + "-" + service,
                "networks": [service, "testdb"],
            }
        override.write_text(json.dumps(definition), encoding="utf-8")
        compose = [docker, "compose", "--project-name", prefix, "--env-file", str(release),
                   "--env-file", str(runtime), "--file", str(DEPLOY / "compose.yaml"), "--file", str(override)]
        ports = {"NOMI_" + s.upper() + "_PORT": "0" for s in SERVICES}
        try:
            sources = {"academic": ROOT, "assistant": args.assistant_source.resolve(), "notifications": args.notifications_source.resolve()}
            for service, source in sources.items():
                image = prefix + "-" + service + ":test"
                dc("build", "--tag", image, str(source), timeout=600)
                images.append(image)
            release.write_text("".join(f"NOMI_{service.upper()}_IMAGE={image}\n" for service, image in zip(SERVICES, images)) +
                               "".join(f"{key}={value}\n" for key, value in ports.items()), encoding="utf-8")
            dc("network", "create", network)
            network_created = True
            pg_env = directory / "postgres.env"
            pg_env.write_text("POSTGRES_PASSWORD=" + password + "\n", encoding="utf-8")
            pg_env.chmod(0o600)
            dc("run", "--detach", "--name", pg, "--network", network, "--tmpfs", "/var/lib/postgresql", "--env-file", str(pg_env), POSTGRES)
            resources.append(pg)
            for _ in range(90):
                if dc("exec", pg, "pg_isready", "-h", "127.0.0.1", "-U", "postgres", check=False).returncode == 0:
                    break
                time.sleep(1)
            else:
                raise RuntimeError("PostgreSQL did not become ready.")
            sql = ""
            for service in SERVICES:
                sql += (f"CREATE ROLE nomi_{service}_app LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB NOCREATEROLE;\n"
                        f"CREATE DATABASE nomi_{service};\nREVOKE ALL ON DATABASE nomi_{service} FROM PUBLIC;\n"
                        f"GRANT CONNECT ON DATABASE nomi_{service} TO nomi_{service}_app;\n")
            dc("exec", "-i", pg, "psql", "-X", "-U", "postgres", "-v", "ON_ERROR_STOP=1", input=sql)
            run(compose + ["up", "--detach", "--no-build", "--wait", "--wait-timeout", "150"], timeout=200)
            before = {}
            for service in SERVICES:
                item = json.loads(dc("inspect", prefix + "-" + service).stdout)[0]
                before[service] = item["Id"]
                assert item["State"]["Health"]["Status"] == "healthy"
                assert item["HostConfig"]["Memory"] == 384 * 1024 * 1024
                assert all(binding["HostIp"] == "127.0.0.1" for bindings in item["HostConfig"]["PortBindings"].values() for binding in bindings)
                mount = next(m for m in item["Mounts"] if m["Destination"] == "/app/certs/global-bundle.pem")
                assert mount["RW"] is False
                values = dict(value.split("=", 1) for value in item["Config"]["Env"])
                assert "ConnectionStrings__" + service.capitalize() in values
                assert all("ConnectionStrings__" + other.capitalize() not in values for other in SERVICES if other != service)
            print("Três APIs reais saudáveis: portas somente loopback, memória limitada, certificado readonly e configurações isoladas.", flush=True)
            # Change only one service env file to verify recreation and failure detection.
            assistant_env.write_text(original_env.replace(password, "intentionally-invalid"), encoding="utf-8")
            failed = run(compose + ["up", "--detach", "--no-deps", "--force-recreate", "--wait", "--wait-timeout", "60", "assistant"], check=False, timeout=90)
            assert failed.returncode != 0, "Compose accepted an unhealthy database connection."
            for service in ("academic", "notifications"):
                item = json.loads(dc("inspect", prefix + "-" + service).stdout)[0]
                assert item["Id"] == before[service] and item["State"]["Health"]["Status"] == "healthy"
            assistant_env.write_text(original_env, encoding="utf-8")
            run(compose + ["up", "--detach", "--no-deps", "--force-recreate", "--wait", "--wait-timeout", "120", "assistant"], timeout=150)
            print("Falha de readiness rejeitada; recuperação por recriação validada, sem reiniciar os outros serviços.", flush=True)
        finally:
            run(compose + ["down", "--remove-orphans"], check=False)
            for name in resources:
                dc("rm", "--force", name, check=False)
            if network_created:
                dc("network", "rm", network, check=False)
            for image in images:
                dc("image", "rm", image, check=False)


if __name__ == "__main__":
    main()

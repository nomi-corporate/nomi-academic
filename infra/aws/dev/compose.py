"""Operate the development Compose stack without printing resolved secrets."""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess

ROOT = Path(__file__).resolve().parent
SERVICES = ("academic", "assistant", "notifications")
REGISTRY = "338128499585.dkr.ecr.us-east-2.amazonaws.com"


def read_settings(path, expected):
    values = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        key, separator, value = line.partition("=")
        if not separator or key not in expected or key in values:
            raise ValueError(f"Arquivo {path.name} contém chave desconhecida, duplicada ou linha inválida.")
        values[key] = value
    if set(values) != set(expected):
        raise ValueError(f"Arquivo {path.name} está incompleto.")
    return values


def validate_files(release_path, runtime_path):
    release = read_settings(release_path, {"NOMI_" + s.upper() + "_IMAGE" for s in SERVICES})
    runtime = read_settings(runtime_path, {"NOMI_SECRETS_DIR", "NOMI_RDS_CA"})
    for service in SERVICES:
        image = release["NOMI_" + service.upper() + "_IMAGE"]
        pattern = re.escape(REGISTRY + "/nomi-" + service) + r"@sha256:[a-f0-9]{64}"
        if not re.fullmatch(pattern, image):
            raise ValueError(f"Imagem de {service} deve usar o repositório esperado e digest SHA-256.")
    secrets = Path(runtime["NOMI_SECRETS_DIR"])
    certificate = Path(runtime["NOMI_RDS_CA"])
    if not secrets.is_absolute() or not certificate.is_absolute():
        raise ValueError("Diretório de credenciais e certificado devem usar caminhos absolutos.")
    if not secrets.is_dir() or not certificate.is_file():
        raise ValueError("Diretório de credenciais ou certificado não encontrado.")
    if os.name != "nt" and stat.S_IMODE(secrets.stat().st_mode) & 0o077:
        raise ValueError("O diretório de credenciais deve ter permissão 700.")
    for service in SERVICES:
        path = secrets / (service + ".env")
        expected = "ConnectionStrings__" + service.capitalize()
        settings = read_settings(path, {expected})
        if not settings[expected].strip():
            raise ValueError(f"Conexão de {service} está vazia.")
        if os.name != "nt" and stat.S_IMODE(path.stat().st_mode) & 0o077:
            raise ValueError(f"O arquivo de {service} deve ter permissão 600.")
    return release, runtime


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--release", type=Path, default=ROOT / "release.env")
    parser.add_argument("--runtime", type=Path, default=ROOT / "runtime.env")
    parser.add_argument("action", choices=["check", "status", "pull", "up", "stop", "logs"])
    parser.add_argument("services", nargs="*", help="academic, assistant e/ou notifications")
    args = parser.parse_args()
    if any(service not in SERVICES for service in args.services):
        parser.error("Serviço inválido. Use academic, assistant ou notifications.")
    docker = shutil.which("docker")
    if not docker:
        parser.error("Docker não encontrado.")
    release = args.release.expanduser().resolve()
    runtime = args.runtime.expanduser().resolve()
    try:
        validate_files(release, runtime)
    except (OSError, ValueError) as error:
        parser.error(str(error))
    env = {key: value for key, value in os.environ.items()
           if not key.startswith("NOMI_") and not key.startswith("COMPOSE_")}
    prefix = [docker, "compose", "--project-name", "nomi-dev", "--env-file", str(release),
              "--env-file", str(runtime), "--file", str(ROOT / "compose.yaml")]
    version = subprocess.run([docker, "compose", "version", "--format", "json"],
                             text=True, capture_output=True, env=env)
    try:
        value = json.loads(version.stdout)["version"].lstrip("v")
        major, minor = (int(part) for part in value.split(".")[:2])
        if (major, minor) < (2, 30):
            raise ValueError()
    except (ValueError, KeyError, TypeError):
        parser.error("Instale o plugin Docker Compose 2.30 ou superior antes de continuar.")
    # Never print compose config: it expands env_file values, including passwords.
    result = subprocess.run(prefix + ["config", "--quiet"], env=env, capture_output=True)
    if result.returncode:
        raise SystemExit("Compose recusou a configuração. Confira caminhos e formato dos arquivos; senhas não foram exibidas.")
    if args.action == "check":
        print("Configuração válida: três imagens por digest, credenciais separadas e certificado encontrado.")
        print("Nenhum contêiner foi iniciado. RDS/TLS serão verificados pelo readiness após o up.")
        return
    operations = {
        "status": ["ps"],
        "pull": ["pull"],
        "up": ["up", "--detach", "--no-build", "--no-deps", "--force-recreate", "--wait", "--wait-timeout", "120"],
        "stop": ["stop"],
        "logs": ["logs", "--tail", "80", "--no-color"],
    }
    result = subprocess.run(prefix + operations[args.action] + args.services, env=env)
    if result.returncode:
        raise SystemExit(result.returncode)


if __name__ == "__main__":
    main()

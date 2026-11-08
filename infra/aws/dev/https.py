"""Operate only the optional HTTPS proxy; never recreate API containers."""
import argparse
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

from compose import read_settings

ROOT = Path(__file__).resolve().parent


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--release", type=Path, default=ROOT / "release.env")
    parser.add_argument("--runtime", type=Path, default=ROOT / "runtime.env")
    parser.add_argument("--https", type=Path, default=ROOT / "https.env")
    parser.add_argument("action", choices=["check", "pull", "up", "status", "logs", "stop"])
    args = parser.parse_args()
    docker = shutil.which("docker")
    if not docker:
        parser.error("Docker não encontrado.")
    try:
        settings = read_settings(args.https, {"NOMI_DOMAIN"})
    except (OSError, ValueError) as error:
        parser.error(str(error))
    domain = settings["NOMI_DOMAIN"]
    if not re.fullmatch(r"[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.duckdns\.org", domain):
        parser.error("NOMI_DOMAIN deve ser um subdomínio DuckDNS simples, sem esquema, porta ou caminho.")
    base = subprocess.run([sys.executable, str(ROOT / "compose.py"), "--release", str(args.release),
                           "--runtime", str(args.runtime), "check"], capture_output=True, text=True)
    if base.returncode:
        raise SystemExit(base.stderr or "Validação da configuração base falhou.")
    env = {key: value for key, value in os.environ.items() if not key.startswith(("NOMI_", "COMPOSE_"))}
    prefix = [docker, "compose", "--project-name", "nomi-dev", "--env-file", str(args.release.resolve()),
              "--env-file", str(args.runtime.resolve()), "--env-file", str(args.https.resolve()),
              "--file", str(ROOT / "compose.yaml"), "--file", str(ROOT / "compose.https.yaml")]
    validated = subprocess.run(prefix + ["config", "--quiet"], capture_output=True, env=env)
    if validated.returncode:
        raise SystemExit("Configuração HTTPS inválida. Confira arquivos; valores de credenciais não foram exibidos.")
    if args.action == "check":
        result = subprocess.run(prefix + ["run", "--rm", "--no-deps", "caddy", "caddy", "validate",
                                          "--config", "/etc/caddy/Caddyfile", "--adapter", "caddyfile"], env=env)
        if result.returncode:
            raise SystemExit(result.returncode)
        print("Configuração Caddy válida. DNS, portas públicas e certificado ACME ainda precisam de validação na EC2.")
        return
    commands = {
        "pull": ["pull", "caddy"],
        "up": ["up", "--detach", "--no-deps", "--force-recreate", "caddy"],
        "status": ["ps", "caddy"],
        "logs": ["logs", "--tail", "100", "--no-color", "caddy"],
        "stop": ["stop", "caddy"],
    }
    result = subprocess.run(prefix + commands[args.action], env=env)
    if result.returncode:
        raise SystemExit(result.returncode)


if __name__ == "__main__":
    main()

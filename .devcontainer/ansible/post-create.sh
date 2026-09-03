#!/usr/bin/env bash
#
# Wird einmalig nach dem Erstellen des Dev Containers ausgeführt.
# Aufgaben:
#   1. SSH-Konfiguration vom Host übernehmen (ohne private Schlüssel zu kopieren)
#   2. Ansible-Collections aus requirements.yml installieren
#   3. Kurze Statusausgabe, damit Fehler sofort sichtbar sind
set -euo pipefail

ANSIBLE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../infrastructure/ansible" && pwd)"

echo "==> SSH-Konfiguration vorbereiten"
mkdir -p "${HOME}/.ssh"
chmod 700 "${HOME}/.ssh"
# ~/.ssh-host ist der read-only Mount des Host-Verzeichnisses. Wir übernehmen
# nur known_hosts (Host-Key-Checking bleibt aktiv). Private Schlüssel werden
# bewusst NICHT kopiert - dafür wird der SSH-Agent des Hosts weitergereicht.
if [[ -f "${HOME}/.ssh-host/known_hosts" && ! -f "${HOME}/.ssh/known_hosts" ]]; then
  cp "${HOME}/.ssh-host/known_hosts" "${HOME}/.ssh/known_hosts"
  chmod 600 "${HOME}/.ssh/known_hosts"
fi

echo "==> Ansible-Collections installieren"
cd "${ANSIBLE_DIR}"
ansible-galaxy collection install -r requirements.yml

echo "==> Versionen"
ansible --version | head -n 1
ansible-lint --version
kubectl version --client=true -o yaml | grep -m1 gitVersion || true
helm version --short

cat <<'EOF'

Dev Container ist bereit.

Nächste Schritte:
  cd infrastructure/ansible
  make help

EOF

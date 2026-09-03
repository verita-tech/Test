# syntax=docker/dockerfile:1
#
# Arbeitsumgebung für die Ansible-Automatisierung.
# Basis ist das offizielle Dev-Container-Python-Image (enthält bereits den
# unprivilegierten Benutzer "vscode", git, sudo und eine sinnvolle Shell).
FROM mcr.microsoft.com/devcontainers/python:1-3.12-bookworm

ARG KUBECTL_VERSION=v1.32.3
ARG HELM_VERSION=v3.17.2

ENV DEBIAN_FRONTEND=noninteractive \
    PIP_DISABLE_PIP_VERSION_CHECK=1 \
    PIP_NO_CACHE_DIR=1

# Systempakete:
#   openssh-client  -> Verbindung zu den Zielsystemen
#   sshpass         -> nur für das einmalige Bootstrapping mit Passwort
#   whois           -> liefert "mkpasswd" zum Erzeugen von Passwort-Hashes
#   rsync           -> synchronize-Modul, Datei-Transfers
#   jq / curl / make/ shellcheck -> Hilfswerkzeuge und Linting
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        openssh-client \
        sshpass \
        whois \
        rsync \
        curl \
        jq \
        make \
        shellcheck \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# kubectl (gepinnt + Checksum-Verifikation)
RUN set -eux; \
    arch="$(dpkg --print-architecture)"; \
    curl -fsSLo /tmp/kubectl "https://dl.k8s.io/release/${KUBECTL_VERSION}/bin/linux/${arch}/kubectl"; \
    curl -fsSLo /tmp/kubectl.sha256 "https://dl.k8s.io/release/${KUBECTL_VERSION}/bin/linux/${arch}/kubectl.sha256"; \
    echo "$(cat /tmp/kubectl.sha256)  /tmp/kubectl" | sha256sum --check --status; \
    install -o root -g root -m 0755 /tmp/kubectl /usr/local/bin/kubectl; \
    rm -f /tmp/kubectl /tmp/kubectl.sha256

# Helm (gepinnt)
RUN set -eux; \
    arch="$(dpkg --print-architecture)"; \
    curl -fsSLo /tmp/helm.tgz "https://get.helm.sh/helm-${HELM_VERSION}-linux-${arch}.tar.gz"; \
    tar -xzf /tmp/helm.tgz -C /tmp; \
    install -o root -g root -m 0755 "/tmp/linux-${arch}/helm" /usr/local/bin/helm; \
    rm -rf /tmp/helm.tgz "/tmp/linux-${arch}"

# Python-Werkzeuge (ansible-core, ansible-lint, yamllint, ...) global installieren,
# damit sie unabhängig vom Home-Verzeichnis des Benutzers verfügbar sind.
COPY requirements.txt /tmp/requirements.txt
RUN pip install --no-cache-dir -r /tmp/requirements.txt \
    && rm -f /tmp/requirements.txt

# Bash-Completion für die täglichen Werkzeuge
RUN set -eux; \
    kubectl completion bash > /etc/bash_completion.d/kubectl; \
    helm completion bash > /etc/bash_completion.d/helm

USER vscode

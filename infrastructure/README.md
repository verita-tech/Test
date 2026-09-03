# Infrastruktur

Alles, was zum Betrieb der Anwendungen dieses Repositories gehört – getrennt
vom Anwendungscode (`Test.Web`, `Test.Web.Api`).

| Verzeichnis | Inhalt |
|---|---|
| [`ansible/`](ansible/README.md) | Automatisierte Einrichtung des K3s-Servers (Ubuntu 26.04): Benutzerkonzept, Härtung, Firewall, Kubernetes. |

Die Arbeitsumgebung dafür ist der Dev Container **„Ansible / K3s“**
(`.devcontainer/ansible/`): In VS Code `F1` → *Dev Containers: Reopen in
Container* → „Ansible / K3s“.

Details und die Schritt-für-Schritt-Anleitung stehen in
[`ansible/README.md`](ansible/README.md).

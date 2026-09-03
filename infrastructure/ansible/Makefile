# Kurzbefehle für den Alltag. Alle Ziele werden aus diesem Verzeichnis
# ausgeführt (der Dev Container bringt alle Werkzeuge mit).
SHELL := /bin/bash

INVENTORY ?= inventories/production/hosts.yml
LIMIT     ?= all
EXTRA     ?=

# Vault-Passwort: liegt .vault-pass vor, wird sie genutzt, sonst wird gefragt.
VAULT_ARGS := $(if $(wildcard .vault-pass),--vault-password-file .vault-pass,--ask-vault-pass)
ANSIBLE_PLAYBOOK := ansible-playbook -i $(INVENTORY) --limit $(LIMIT) $(VAULT_ARGS) $(EXTRA)

.DEFAULT_GOAL := help

.PHONY: help
help: ## Diese Übersicht anzeigen
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) \
		| awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-16s\033[0m %s\n", $$1, $$2}'

.PHONY: deps
deps: ## Ansible-Collections installieren/aktualisieren
	ansible-galaxy collection install -r requirements.yml --upgrade

.PHONY: lint
lint: ## yamllint + ansible-lint über das gesamte Verzeichnis
	yamllint .
	ansible-lint

.PHONY: ping
ping: ## Erreichbarkeit und Rechte aller Hosts prüfen
	ansible -i $(INVENTORY) --limit $(LIMIT) all -m ansible.builtin.ping

.PHONY: facts
facts: ## Systeminformationen eines Hosts anzeigen (LIMIT=<host>)
	ansible -i $(INVENTORY) --limit $(LIMIT) all -m ansible.builtin.setup

.PHONY: bootstrap
bootstrap: ## Einmaliges Anlegen des Automationsbenutzers (Erst-Zugang nötig)
	$(ANSIBLE_PLAYBOOK) playbooks/bootstrap.yml --ask-pass --ask-become-pass

.PHONY: check
check: ## Trockenlauf mit Diff-Ausgabe - ändert nichts
	$(ANSIBLE_PLAYBOOK) playbooks/site.yml --check --diff

.PHONY: deploy
deploy: ## Soll-Zustand anwenden
	$(ANSIBLE_PLAYBOOK) playbooks/site.yml --diff

.PHONY: deploy-k3s
deploy-k3s: ## Nur die K3s-Rolle anwenden
	$(ANSIBLE_PLAYBOOK) playbooks/site.yml --diff --tags k3s

.PHONY: deploy-users
deploy-users: ## Nur Benutzer/Gruppen/sudo anwenden (On-/Offboarding)
	$(ANSIBLE_PLAYBOOK) playbooks/site.yml --diff --tags users

.PHONY: kubeconfig
kubeconfig: ## kubeconfig vom Server nach .kube/ holen
	$(ANSIBLE_PLAYBOOK) playbooks/site.yml --tags kubeconfig \
		-e k3s_server_fetch_kubeconfig=true

.PHONY: vault-edit
vault-edit: ## Verschlüsselte Geheimnisse bearbeiten
	ansible-vault edit $(VAULT_ARGS) inventories/production/group_vars/all/vault.yml

.PHONY: vault-create
vault-create: ## Vault-Datei aus der Vorlage erzeugen und verschlüsseln
	cp -n inventories/production/group_vars/all/vault.yml.example \
	      inventories/production/group_vars/all/vault.yml
	$${EDITOR:-vi} inventories/production/group_vars/all/vault.yml
	ansible-vault encrypt $(VAULT_ARGS) inventories/production/group_vars/all/vault.yml

.PHONY: known-hosts
known-hosts: ## Host-Keys der Inventory-Hosts in ~/.ssh/known_hosts aufnehmen
	@mkdir -p ~/.ssh
	@ansible -i $(INVENTORY) all --list-hosts | tail -n +2 | tr -d ' ' | while read -r h; do \
		ip=$$(ansible -i $(INVENTORY) "$$h" -m debug -a "var=ansible_host" 2>/dev/null \
			| grep -oE '[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+' | head -n1); \
		[ -n "$$ip" ] && ssh-keyscan -H "$$ip" >> ~/.ssh/known_hosts; \
	done
	@echo "known_hosts aktualisiert - Fingerprints bitte gegen die Server-Dokumentation prüfen."

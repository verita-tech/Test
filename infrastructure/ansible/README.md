# K3s-Server-Automatisierung (Ansible)

Vollständige, wiederholbare Einrichtung eines K3s-Servers (Kubernetes) auf
**Ubuntu 26.04 LTS** mit Ansible – entwickelt und ausgeführt aus einem
**VS Code Dev Container**.

Ziel ist ein Server, der später sowohl Infrastruktur-Komponenten (Ingress,
Zertifikate, Monitoring, Datenbanken) als auch die eigenentwickelten
Anwendungen aus diesem Repository trägt.

---

## Inhalt

1. [Überblick](#1-überblick)
2. [Benutzerkonzept](#2-benutzerkonzept)
3. [Verzeichnisstruktur](#3-verzeichnisstruktur)
4. [Dev Container](#4-dev-container)
5. [Erstinbetriebnahme Schritt für Schritt](#5-erstinbetriebnahme-schritt-für-schritt)
6. [Was die einzelnen Rollen tun](#6-was-die-einzelnen-rollen-tun)
7. [Geheimnisse (Ansible Vault)](#7-geheimnisse-ansible-vault)
8. [Betrieb im Alltag](#8-betrieb-im-alltag)
9. [Sicherheitsentscheidungen im Überblick](#9-sicherheitsentscheidungen-im-überblick)
10. [Fehlersuche](#10-fehlersuche)
11. [Nächste Schritte](#11-nächste-schritte)

---

## 1. Überblick

```
   Arbeitsplatz (VS Code)
   └── Dev Container  ──SSH (Key, Port 22)──▶  Ubuntu 26.04 Server
       ansible, ansible-lint,                  ├── ufw (Host-Firewall)
       yamllint, kubectl, helm                 ├── sshd (gehärtet)
                                               ├── fail2ban
                                               └── K3s (Control-Plane + Workloads)
                                                   ├── Infrastruktur (Helm)
                                                   └── eigene Anwendungen
```

Grundprinzipien:

| Prinzip | Umsetzung |
|---|---|
| **Infrastructure as Code** | Der Server wird ausschließlich über dieses Repository verändert. Manuelle Änderungen werden beim nächsten Lauf überschrieben. |
| **Idempotenz** | Jedes Playbook kann beliebig oft laufen und beschreibt einen Soll-Zustand, keine Abfolge von Befehlen. |
| **Least Privilege** | Getrennte Konten für Automatisierung und Menschen; sudo nur, wo nötig; Firewall standardmäßig geschlossen. |
| **Nachvollziehbarkeit** | Jede Änderung ist ein Git-Commit; auf dem Server protokollieren `sshd` (LogLevel VERBOSE) und `sudo` jede Aktion. |
| **Reproduzierbare Werkzeuge** | Alle Werkzeuge und Versionen stecken im Dev Container – kein „läuft nur auf meinem Rechner“. |

---

## 2. Benutzerkonzept

Dies ist der Kern der Anforderung – hier die Umsetzung im Detail.

| Konto | Zweck | Anmeldung | sudo |
|---|---|---|---|
| `ansible` | technischer Benutzer für die Automatisierung | nur SSH-Key, Passwort gesperrt | ja, ohne Passwort (nicht-interaktive Läufe) |
| `mmustermann`, `emusterfrau`, … | personalisierte Konten der Administratoren | nur SSH-Key | **nein** (bewusst) |
| `admin-app` | **zentraler Admin-Account**, gemeinsam genutzt | **kein Zugang von außen**, nur lokal per `su` mit gemeinsamem Passwort | ja, mit Passwortabfrage |

### Der vorgesehene Arbeitsweg

```
Mensch ──SSH mit persönlichem Key──▶ eigenes Konto ──su - admin-app (gemeinsames Passwort)──▶ Admin-Arbeit (sudo)
```

Warum dieser Umweg? Er verbindet zwei Anforderungen, die sich sonst
widersprechen:

* **Nachvollziehbarkeit**: Das Journal zeigt, *welcher Mensch* sich angemeldet
  hat (`sshd` protokolliert Benutzername + Key-Fingerprint) und wer wann nach
  `admin-app` gewechselt ist (`su`-Eintrag in `auth.log`/Journal).
* **Gemeinsamer Arbeitskontext**: Alle Administratoren arbeiten anschließend im
  selben Konto – gleiche Umgebung, gleiche Shell-History, gleiche
  `kubeconfig`.

### Wie „kein Login von außen“ technisch sichergestellt wird

Drei voneinander unabhängige Sperren (Defense in Depth):

1. **`AllowGroups ssh-users`** in der sshd-Konfiguration – `admin-app` ist in
   dieser Gruppe nicht Mitglied.
2. **`DenyUsers admin-app`** – explizite Sperre, selbst falls die
   Gruppenmitgliedschaft einmal versehentlich gesetzt würde.
3. **Keine `authorized_keys`** für das Konto; die Datei wird bei jedem Lauf
   aktiv entfernt.

Zusätzlich regelt ein PAM-Block in `/etc/pam.d/su`, **wer** überhaupt nach
`admin-app` wechseln darf: nur Mitglieder der Gruppe `admin-app-su`
(gesteuert über `admin_app: true` je Person im Inventory).

```
auth  [success=ignore default=1]  pam_succeed_if.so quiet user = admin-app
auth  requisite                   pam_succeed_if.so quiet use_uid user ingroup admin-app-su
```

Zeile 1 prüft das *Ziel* des `su`-Aufrufs, Zeile 2 den *Aufrufer*. Jedes andere
`su` bleibt unverändert; `root` ist durch das darüberstehende `pam_rootok.so`
ohnehin ausgenommen.

### Hinweis zur Schreibweise „Admin-App“

Linux-Benutzernamen müssen laut `NAME_REGEX` (`/etc/adduser.conf`,
`/etc/login.defs`) kleingeschrieben sein; `useradd` lehnt Großbuchstaben ab
bzw. erzeugt schwer wartbare Sonderfälle. Der Account heißt deshalb
**`admin-app`**. Der Name ist an einer einzigen Stelle konfigurierbar:
`site_admin_app_user` in `inventories/production/group_vars/all/main.yml`.

---

## 3. Verzeichnisstruktur

```
infrastructure/ansible/
├── ansible.cfg                 # zentrale Ansible-Einstellungen (Inventory, SSH, Callbacks)
├── Makefile                    # Kurzbefehle: make check / deploy / lint / vault-edit …
├── requirements.yml            # benötigte Ansible-Collections
├── .ansible-lint / .yamllint   # Linting-Regeln (Profil "production")
│
├── inventories/production/
│   ├── hosts.yml               # welche Server gibt es, wie heißen sie, welche IP
│   ├── group_vars/
│   │   ├── all/main.yml        # zentrale Stellschrauben (Benutzer, Netze, Ports)
│   │   ├── all/vault.yml       # VERSCHLÜSSELT: Passwort-Hash des Admin-Accounts
│   │   └── k3s_servers/main.yml# K3s-Version, deaktivierte Komponenten, TLS-Namen
│   └── host_vars/
│       └── k3s-prod-01.yml     # alles, was nur diese eine Maschine betrifft
│
├── playbooks/
│   ├── bootstrap.yml           # EINMALIG: legt den Automationsbenutzer an
│   └── site.yml                # Haupt-Playbook: vollständiger Soll-Zustand
│
└── roles/
    ├── common/                 # Grundzustand (Pakete, Zeit, Locale, Logging, Updates, Swap)
    ├── users/                  # Benutzer, Gruppen, sudo, su-Beschränkung
    ├── ssh_hardening/          # sshd-Härtung + fail2ban
    ├── firewall/               # ufw-Regelwerk inkl. K3s-Freigaben
    └── k3s_server/             # K3s-Installation, Konfiguration, kubeconfig
```

Jede Rolle folgt dem Standard-Layout:

| Verzeichnis | Inhalt |
|---|---|
| `defaults/main.yml` | Alle Variablen mit sicheren Vorgaben – **die Schnittstelle der Rolle**. Nur diese Werte werden im Inventory überschrieben. |
| `vars/main.yml` | Berechnete Werte, die *nicht* überschrieben werden sollen. |
| `tasks/` | Die eigentlichen Schritte, thematisch auf mehrere Dateien aufgeteilt. |
| `handlers/` | Reaktionen auf Änderungen (Dienst neu starten) – laufen nur, wenn wirklich etwas geändert wurde. |
| `templates/` | Konfigurationsdateien als Jinja2-Vorlagen, jeweils mit `{{ ansible_managed }}`-Kopfzeile. |
| `meta/main.yml` | Metadaten und Abhängigkeiten. |
| `meta/argument_specs.yml` | Formale Beschreibung der Variablen – Ansible validiert Eingaben automatisch (Rolle `users`). |

---

## 4. Dev Container

Die gesamte Werkzeugkette steckt in `.devcontainer/ansible/`:

| Datei | Inhalt |
|---|---|
| `devcontainer.json` | Container-Definition, VS Code-Erweiterungen (Ansible, YAML, Kubernetes), SSH-Mount, `ANSIBLE_CONFIG`. |
| `Dockerfile` | Basis-Image + `openssh-client`, `sshpass`, `whois` (`mkpasswd`), `kubectl` und `helm` in **fest gepinnten** Versionen inkl. Checksum-Prüfung. |
| `requirements.txt` | `ansible-core`, `ansible-lint`, `yamllint` und die von Modulen benötigten Python-Bibliotheken. |
| `post-create.sh` | Läuft einmalig nach dem Bau: übernimmt `known_hosts`, installiert die Collections, zeigt die Versionen an. |

**Starten:** VS Code öffnen → `F1` → *Dev Containers: Reopen in Container* →
**„Ansible / K3s“** auswählen. (Das Repository enthält bewusst mehrere
Dev-Container-Konfigurationen nebeneinander, damit die .NET-Entwicklung davon
unberührt bleibt.)

**SSH-Schlüssel:** Der private Schlüssel wird **nicht** in den Container
kopiert. VS Code reicht den SSH-Agent des Hosts durch; `~/.ssh` wird zusätzlich
schreibgeschützt unter `~/.ssh-host` eingehängt. Vor dem ersten Lauf auf dem
Host sicherstellen:

```bash
ssh-add ~/.ssh/id_ed25519       # Schlüssel in den Agent laden
```

---

## 5. Erstinbetriebnahme Schritt für Schritt

### Schritt 1 – Inventory anpassen

`inventories/production/hosts.yml`: IP bzw. DNS-Namen des bereitgestellten
Servers eintragen.

`inventories/production/group_vars/all/main.yml`:

* `site_management_networks` – aus welchen Netzen SSH und Kubernetes-API
  erreichbar sein sollen (Firmennetz/VPN, **nicht** `0.0.0.0/0`).
* `site_admin_users` – die realen Administratoren mit ihren **öffentlichen**
  SSH-Schlüsseln.
* `site_automation_ssh_keys` – Schlüssel, mit denen Ansible arbeitet.

> Alle Platzhalter enthalten die Zeichenkette `BITTE_ERSETZEN`. Das
> Bootstrap-Playbook bricht ab, solange sie noch vorhanden ist.

### Schritt 2 – Passwort des zentralen Admin-Accounts festlegen

```bash
cd infrastructure/ansible
mkpasswd --method=yescrypt          # gemeinsames Passwort eingeben, Hash kopieren
make vault-create                   # Vorlage öffnen, Hash eintragen, verschlüsseln
```

Das **Klartext-Passwort** gehört in den Passwort-Tresor des Unternehmens und
wird dort mit den berechtigten Personen geteilt. Im Repository liegt nur der
Hash – und dieser zusätzlich verschlüsselt.

### Schritt 3 – Host-Key einmalig verifizieren

```bash
make known-hosts
```

Fingerprint gegen die Server-Dokumentation des Hosters prüfen.
(`host_key_checking` bleibt aktiv – das ist der Schutz gegen
Man-in-the-Middle-Angriffe.)

### Schritt 4 – Bootstrapping (einmalig)

Legt den technischen Benutzer `ansible` an. Dafür wird noch der Erst-Zugang des
Hosters benutzt:

```bash
make bootstrap EXTRA="-e ansible_user=ubuntu"
```

### Schritt 5 – Trockenlauf und Anwendung

```bash
make check      # zeigt alle geplanten Änderungen (--check --diff), ändert nichts
make deploy     # wendet den Soll-Zustand an
```

### Schritt 6 – Ergebnis prüfen

```bash
ssh mmustermann@<server>        # persönlicher Zugang mit Key
su - admin-app                  # gemeinsames Passwort
kubectl get nodes               # Knoten muss "Ready" sein
```

Gegenprobe (muss **fehlschlagen**):

```bash
ssh admin-app@<server>          # Permission denied (publickey)
```

---

## 6. Was die einzelnen Rollen tun

### `common` – Grundzustand

* prüft per `assert`, dass wirklich Ubuntu ≥ 24.04 läuft (Abbruch statt
  unvorhersehbarem Verhalten auf fremden Distributionen);
* installiert die Basiswerkzeuge und `python3-apt`;
* setzt Zeitzone (`Europe/Berlin`), aktiviert `systemd-timesyncd` und erzeugt
  die Locale – wichtig, damit Logzeitstempel über alle Systeme vergleichbar sind;
* setzt Hostname und den passenden `/etc/hosts`-Eintrag;
* begrenzt das Journal (persistent, 1 GB, 30 Tage), damit `/var` nicht vollläuft;
* konfiguriert **unattended-upgrades** ausschließlich für Sicherheitsquellen –
  ohne automatischen Reboot (Standard), damit ein Neustart eine bewusste
  Entscheidung bleibt;
* deaktiviert Swap dauerhaft (Voraussetzung für das Kubelet).

### `users` – Benutzerkonzept

* legt die Gruppen `ssh-users`, `k3s-admin` und `admin-app-su` an;
* erstellt den technischen Benutzer `ansible` (Key-only, `NOPASSWD`-sudo);
* erstellt je Person ein Konto mit `authorized_key … exclusive: true` –
  entfernte Schlüssel verschwinden dadurch beim nächsten Lauf automatisch
  vom Server (**Offboarding = Änderung im Git**);
* erstellt den zentralen `admin-app` mit Passwort-Hash aus dem Vault,
  ohne SSH-Zugang, mit sudo **mit** Passwortabfrage;
* schränkt `su - admin-app` per PAM auf die Gruppe `admin-app-su` ein;
* schreibt alle sudo-Regeln als Dateien unter `/etc/sudoers.d/` und validiert
  sie mit `visudo --check`, bevor sie aktiv werden – eine kaputte
  sudo-Konfiguration wäre sonst nur noch über die Konsole reparierbar.

### `ssh_hardening` – Fernzugang

* schreibt die Konfiguration als **Drop-in** nach
  `/etc/ssh/sshd_config.d/10-hardening.conf` (in sshd gewinnt der zuerst
  gelesene Wert; die distributionsverwaltete Hauptdatei bleibt unangetastet);
* prüft die Datei mit `sshd -t` **vor** dem Aktivieren und zusätzlich die
  Gesamtkonfiguration – so kann ein Tippfehler den Zugang nicht sperren;
* deaktiviert Root-Login und Passwort-Anmeldung, begrenzt Anmeldeversuche
  und Zeitfenster, schaltet Agent-/X11-Forwarding ab und beschränkt
  Schlüsseltausch, Chiffren und MACs auf aktuelle Verfahren;
* berücksichtigt die **Socket-Aktivierung** von `ssh` (ab Ubuntu 22.10): ein
  abweichender Port wird per Drop-in in `ssh.socket` gesetzt, nicht in der
  `sshd_config`, wo er wirkungslos wäre;
* installiert `fail2ban` mit `backend = systemd` (Ubuntu liefert seit 24.04
  kein rsyslog mehr mit) und nimmt die Management-Netze von der Sperre aus.

### `firewall` – Paketfilter

* Standardrichtlinie: eingehend **deny**, ausgehend allow, weitergeleitet allow
  (Letzteres ist für Container-Netzwerke zwingend);
* SSH nur aus den Management-Netzen und mit `limit` (Rate-Limit);
* Kubernetes-API (6443) nur aus den Management-Netzen;
* HTTP/HTTPS für die späteren Anwendungen;
* Freigabe des Pod- (`10.42.0.0/16`) und Service-Netzes (`10.43.0.0/16`) –
  ohne diese Regeln blockiert ufw die Cluster-interne Kommunikation
  (CoreDNS, API-Zugriffe aus Pods);
* **erst am Ende** wird die Firewall aktiviert – sonst würde der laufende
  Ansible-Lauf seine eigene Verbindung kappen.

### `k3s_server` – Kubernetes

* lädt die Kernel-Module `overlay` und `br_netfilter` und setzt die nötigen
  `sysctl`-Werte (klassische Härtungs-`sysctl`s wie `rp_filter=strict` werden
  bewusst **nicht** gesetzt – sie brechen das Flannel-Overlay);
* schreibt `/etc/rancher/k3s/config.yaml` **vor** der Installation, damit K3s
  bereits beim ersten Start korrekt konfiguriert hochkommt. Sämtliche
  Parameter stehen in dieser Datei und nicht in der systemd-Unit;
* installiert die **fest gepinnte** Version über das offizielle Skript und
  führt es nur aus, wenn die gewünschte Version noch nicht läuft
  (`k3s --version`-Vergleich) – dadurch ist auch ein Upgrade nur eine
  Änderung von `k3s_server_version`;
* deaktiviert Traefik: der Ingress-Controller wird später bewusst per Helm
  versioniert installiert;
* aktiviert `secrets-encryption` (Secrets werden verschlüsselt abgelegt) und
  setzt Kubelet-Reserven, damit ein entgleister Pod den Knoten nicht mitreißt;
* macht die `kubeconfig` für die Gruppe `k3s-admin` lesbar. Weil K3s die Datei
  bei jedem Start neu schreibt, korrigiert ein systemd-`ExecStartPost`-Hook die
  Rechte dauerhaft; `/etc/profile.d/99-k3s.sh` setzt `KUBECONFIG` automatisch;
* wartet zum Abschluss auf `readyz` und `kubectl wait --for=condition=Ready`
  und gibt eine Zusammenfassung aus – der Lauf ist erst grün, wenn der Cluster
  wirklich läuft.

---

## 7. Geheimnisse (Ansible Vault)

* **Im Repository liegen keine Klartext-Geheimnisse.** Der Passwort-Hash des
  Admin-Accounts steht verschlüsselt in
  `inventories/production/group_vars/all/vault.yml`.
* Konvention: Vault-Variablen heißen `vault_*` und werden in `main.yml` auf die
  Rollen-Variablen gemappt. Dadurch ist im Klartext sichtbar, **welcher Wert
  aus dem Vault stammt** – ein häufig empfohlener Ansible-Kniff.
* Das Vault-Passwort selbst gehört in den Passwort-Tresor des Unternehmens.
  Lokal kann es in `.vault-pass` abgelegt werden (per `.gitignore`
  ausgeschlossen); ohne diese Datei fragt `make` interaktiv danach.

```bash
make vault-edit                    # Geheimnisse bearbeiten
ansible-vault rekey inventories/production/group_vars/all/vault.yml   # Vault-Passwort wechseln
```

**Passwort des Admin-Accounts rotieren:** neuen Hash erzeugen, per
`make vault-edit` eintragen, `make deploy-users` ausführen – die Rolle setzt
das Passwort bei jedem Lauf neu (`update_password: always`), Abweichungen auf
dem Server werden damit automatisch korrigiert.

---

## 8. Betrieb im Alltag

| Aufgabe | Befehl |
|---|---|
| Änderungen vorab ansehen | `make check` |
| Soll-Zustand anwenden | `make deploy` |
| Nur Benutzer aktualisieren (On-/Offboarding) | `make deploy-users` |
| Nur K3s aktualisieren | `make deploy-k3s` |
| Erreichbarkeit prüfen | `make ping` |
| Linting vor jedem Commit | `make lint` |
| kubeconfig für den Arbeitsplatz holen | `make kubeconfig` |

**Neuen Administrator aufnehmen:** Eintrag in `site_admin_users` ergänzen
(inkl. `admin_app: true`), `make deploy-users`.

**Administrator entfernen:** `state: absent` setzen (oder Eintrag löschen und
`state: absent` als Übergang belassen), `make deploy-users`. Der Account samt
Home-Verzeichnis wird entfernt, die Schlüssel verlieren ihre Wirkung.

**K3s aktualisieren:** `k3s_server_version` in
`group_vars/k3s_servers/main.yml` anheben, `make check`, dann `make deploy-k3s`
in einem Wartungsfenster (der Dienst startet dabei neu).

---

## 9. Sicherheitsentscheidungen im Überblick

| Entscheidung | Begründung |
|---|---|
| Kein Passwort-Login per SSH | Schlüssel sind nicht erratbar; Brute-Force läuft ins Leere. |
| `admin-app` nur lokal per `su` | Erfüllt „gemeinsamer Admin-Account“, ohne ein gemeinsames Passwort dem Internet auszusetzen. |
| Personalisierte Konten ohne sudo | Rechteerhöhung läuft über genau einen, klar protokollierten Pfad. |
| `NOPASSWD` nur für `ansible` | Automatisierung muss nicht-interaktiv laufen; das Konto ist ausschließlich per Schlüssel erreichbar. |
| Firewall standardmäßig geschlossen | Nur explizit freigegebene Dienste sind erreichbar. |
| `secrets-encryption` in K3s | Secrets liegen nicht im Klartext im Datastore. |
| Feste Versionen (K3s, kubectl, helm) | Updates sind bewusste, überprüfbare Änderungen statt Zufall. |
| `exclusive: true` bei SSH-Schlüsseln | Das Repository ist die einzige Wahrheit darüber, wer Zugang hat. |

---

## 10. Fehlersuche

**„Permission denied (publickey)“ nach dem Härten**
Der eigene Benutzer ist nicht in der Gruppe `ssh-users` oder der öffentliche
Schlüssel fehlt im Inventory. Zugang über die Konsole des Hosters, dann
`/etc/ssh/sshd_config.d/10-hardening.conf` prüfen.

**`su - admin-app` sagt „Permission denied“**
Der aufrufende Benutzer ist nicht in der Gruppe `admin-app-su`
(`admin_app: true` im Inventory setzen und `make deploy-users` ausführen).

**`kubectl` meldet „connection refused“ oder fehlende Rechte**
`id` prüfen: Der Benutzer muss in der Gruppe `k3s-admin` sein. Nach einer
Gruppenänderung ist eine neue Anmeldung nötig.

**Host-Key-Fehler beim Ansible-Lauf**
`make known-hosts` ausführen und den Fingerprint verifizieren. Host-Key-Prüfung
wird bewusst nicht deaktiviert.

**Firewall hat mich ausgesperrt**
Über die Konsole des Hosters: `ufw status numbered`, `ufw disable`, Ursache in
`firewall_management_networks` beheben, erneut deployen.

**Ansible-Lauf zeigt Änderungen bei jedem Durchlauf**
Das ist ein Fehler, kein Normalzustand – bitte melden. Idempotenz ist die
Grundannahme dieses Repositories.

---

## 11. Nächste Schritte

Bewusst noch nicht enthalten, aber als nächste Ausbaustufen vorgesehen:

* **Datensicherung**: regelmäßiger Snapshot von `/var/lib/rancher/k3s/server`
  (SQLite-Datastore + Node-Token) auf einen externen Speicher, inkl.
  dokumentiertem Restore-Test.
* **Ingress & Zertifikate**: `ingress-nginx` und `cert-manager` per
  `kubernetes.core.helm` – die Collection ist bereits eingebunden.
* **Monitoring**: `kube-prometheus-stack` oder Anbindung an ein vorhandenes
  Firmen-Monitoring.
* **Deployment der eigenen Anwendungen**: Container-Images aus der
  Firmen-Registry (`k3s_server_registries` vorbereitet), Deployment per Helm-Chart
  oder GitOps (Argo CD / Flux).
* **Hochverfügbarkeit**: Umstieg auf drei Server-Knoten mit eingebettetem etcd
  (`cluster-init`); das Inventory ist mit der Gruppe `k3s_servers` bereits darauf
  vorbereitet.
* **CI**: `make lint` und `make check` als Pipeline-Schritt bei jedem Pull
  Request.

# DAQmxWebApp

Application **Blazor Server** (.NET 8) permettant de configurer, démarrer et monitorer une tâche NI-DAQmx via le NI gRPC Device Server, depuis un navigateur web.

---

## Architecture

```
Navigateur  ←─── Blazor Server (Docker) ←─── gRPC ─── NI gRPC Device Server (Windows hôte)
```

- Le **NI gRPC Device Server** reste sur votre PC Windows (port `31763` par défaut).
- L'application Blazor tourne dans un conteneur Docker for Windows.
- L'interface web est accessible sur **http://localhost:8080**.
- La connexion au serveur hôte se fait via `host.docker.internal`.
- Le client gRPC utilise **`http://`** (sans TLS) vers NI : le runtime .NET doit autoriser **HTTP/2 en clair** (`RuntimeHostConfigurationOption` / `Http2UnencryptedSupport` dans le projet et `Program.cs`). Sans cela, vous obtiendrez une erreur du type *unable to establish HTTP/2 connection*.

---

## Dépannage

- Après modification du code, si Docker semble encore servir l’ancienne version : `docker compose build --no-cache` puis `docker compose up`.
- Erreur **HTTP/2** au premier appel RPC : vérifier que la dernière build est bien celle du dépôt (rebuild local ou image Docker reconstruite).

---

## Démarrage rapide

### 1. Build & run avec Docker Compose

```bash
cd DAQmxWebApp
docker compose up --build
```

### 2. Accéder à l'interface

Ouvrez votre navigateur sur : [http://localhost:8080](http://localhost:8080)

### 3. Utilisation

1. **Configurer** : renseignez l'adresse du serveur (`host.docker.internal`), le canal physique, les plages de tension, etc.
2. **Connecter** : cliquez sur 🔌 Connecter pour établir la connexion gRPC.
3. **Démarrer** : cliquez sur ▶ Démarrer pour lancer la tâche DAQmx et voir le graphique en temps réel.
4. **Arrêter** : cliquez sur ⏹ Arrêter pour stopper la tâche (les paramètres restent modifiables).
5. **Déconnecter** : cliquez sur ⏏ Déconnecter pour fermer le canal gRPC.

---

## Paramètres configurables

| Paramètre | Défaut | Description |
|---|---|---|
| Adresse serveur | `host.docker.internal` | Adresse du NI gRPC Device Server |
| Port | `31763` | Port gRPC |
| Nom de session | `DAQmx-WebApp` | Nom de la tâche DAQmx |
| Canal physique | `Dev1/ai0` | Canal d'entrée analogique |
| Tension min/max | `-10 / +10 V` | Plage de la voie AI |
| Fréquence | `1000 Hz` | Sample rate |
| Échantillons/lecture | `100` | `NumSampsPerChan` par appel Read |
| Multiplicateur buffer | `10` | Taille buffer = samples × multiplicateur |

---

## Développement local (sans Docker)

```bash
dotnet run
```

L'app sera disponible sur http://localhost:5000.  
Dans ce cas, utilisez `localhost` comme adresse serveur (le gRPC server est sur la même machine).

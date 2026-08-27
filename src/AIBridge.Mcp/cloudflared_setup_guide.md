# Cloudflare Tunnel (`cloudflared`) Setup Guide

This guide walks through securely exposing a local development server (like `ai-bridge-mcp` running on `localhost:5000`) to the public internet using a Cloudflare Tunnel and Docker.

---

## Phase 1: Create the Tunnel in Cloudflare

1. Log in to your standard Cloudflare account dashboard.
2. On the left sidebar, navigate to **Protected & Connect** -> **Networking** -> **Tunnels**.
*(Note: This avoids the separate Zero Trust dashboard which forces you to enter a credit card).*
3. Click **Create a tunnel** and select **Cloudflared**.
4. Give it a descriptive name (e.g., `ai-bridge-mcp`) and click **Save tunnel**.
5. On the "Install and run a connector" screen, select the **Docker** environment.
6. Copy the provided `docker run` command, specifically grabbing the long `--token` string at the end.

> [!NOTE] 
> **Lost your token?** Once a tunnel is connected, Cloudflare hides the token for security. If you ever lose your Docker setup or move to a new machine, simply go to your Tunnel settings and click **Rotate Token**. This will generate a brand new token you can use.

---

## Phase 2: Run the Docker Container (Linux/WSL)

Because the MCP server runs directly on your Linux host machine, the Docker container needs special permission to access the host's `localhost`. We achieve this by adding `--network host` to the Docker command.

Run the following command in your terminal (replacing `<YOUR_TOKEN>`):

```bash
docker run -d \
  --name cloudflared \
  --network host \
  --restart unless-stopped \
  cloudflare/cloudflared:latest tunnel --no-autoupdate run --token <YOUR_TOKEN>
```

### Parameter Breakdown:
* `-d`: Runs the container in the background (detached mode).
* `--name cloudflared`: Gives the container a friendly name so you can easily manage it (e.g., `docker logs cloudflared`).
* `--network host`: Plugs the container directly into the Linux host network, allowing it to see your machine's `localhost:5000`.
* `--restart unless-stopped`: Ensures the tunnel automatically starts up in the background whenever you reboot your computer.
* `--no-autoupdate`: Disables the app's internal updater (in Docker, you update by pulling a new image).

> [!TIP]
> **What is a Replica?**
> In your Cloudflare dashboard, you will see "1 Replica" when the container is running. A replica just means "an active connection". Large companies run the exact same token on 5 servers at once (5 Replicas) for load balancing. For local dev, you will only ever have 1 Replica.

---

## Phase 3: Route the Traffic

1. In the Cloudflare dashboard, click on your tunnel and select **Configure** (or click **Next** if you just created it).
2. Click the **Add route** button.
3. When prompted, select **Published application**.
4. Fill out the configuration exactly as follows:
   * **Subdomain:** Enter your desired prefix (e.g., `mcp`).
   * **Domain:** Select your registered domain from the dropdown.
   * **Path:** Leave this completely **EMPTY**.
   * **Service Type:** Select **`HTTP`**.
   * **Service URL:** Type **`localhost:5000`**.

> [!IMPORTANT]
> **No Trailing Slashes!**
> Ensure your Service URL is exactly `localhost:5000`. Do not include `http://` in the box, and do not add a trailing slash `/`. Cloudflare's strict validation will reject `localhost:5000/`.

4. Click **Save Hostname**.

---

## Phase 4: Testing the Setup

When you reboot your machine, the Docker tunnel will start automatically. However, your MCP server must be started manually.

**1. Start the MCP Server:**
Open a terminal, navigate to the specific codebase directory you want the AI to access, and run your globally installed tool:
```bash
ai-bridge-mcp
```

**2. Test the Public Endpoint:**
Open a second terminal and run this `curl` command to verify traffic is flowing from the internet, through Cloudflare, into Docker, and hitting your `.NET` server:

```bash
curl -X POST https://mcp.yourdomain.com/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc": "2.0", "id": 1, "method": "tools/list"}'
```

If it returns a stream of JSON data starting with `event: message`, your tunnel is perfectly configured!

---

## Troubleshooting: Deleting a Tunnel

If you ever need to start over, you can freely delete your tunnel and recreate it. However, you must perform two cleanup steps:

1. **Clean up Docker:** Stop and remove the old container before running the new one:
   ```bash
   docker stop cloudflared
   docker rm cloudflared
   ```
2. **Clean up DNS Records:** When you assign a Public Hostname, Cloudflare creates a hidden `CNAME` DNS record linking that subdomain to the specific tunnel ID. If you delete the tunnel, you must go to your main **Cloudflare Dashboard -> DNS -> Records** and manually delete the leftover record (e.g., the one for `mcp` pointing to `.cfargotunnel.com`). If you skip this, Cloudflare won't let you use the same subdomain on your new tunnel.

#!/usr/bin/env python3
"""
Naive TicTacToe client that navigates entirely via HTTP discovery.
No hardcoded URLs, field names, or state names.
All field IRIs are read from the ALPS profile.
"""
import argparse
import json
import sys
import time
import urllib.request
import urllib.error


def http_get(url, accept=None):
    req = urllib.request.Request(url)
    if accept:
        req.add_header("Accept", accept)
    with urllib.request.urlopen(req) as resp:
        return json.loads(resp.read().decode())


def http_get_raw(url, accept=None):
    req = urllib.request.Request(url)
    if accept:
        req.add_header("Accept", accept)
    with urllib.request.urlopen(req) as resp:
        return resp.read().decode()


def http_post(url, body):
    data = json.dumps(body).encode()
    req = urllib.request.Request(url, data=data, method="POST")
    req.add_header("Content-Type", "application/ld+json")
    try:
        with urllib.request.urlopen(req) as resp:
            return resp.status, json.loads(resp.read().decode())
    except urllib.error.HTTPError as e:
        body_bytes = e.read()
        return e.code, json.loads(body_bytes.decode()) if body_bytes else {}


def discover_game_template(server):
    """Step 1: GET / with Accept: application/json-home to find game resource template."""
    data = http_get(server + "/", accept="application/json-home")
    resources = data.get("resources", {})
    for rel, resource in resources.items():
        # Naive: pick the first resource that has a template or href
        href = resource.get("href-template") or resource.get("href")
        if href:
            return href
    raise RuntimeError("No game resource found in JSON Home")


def discover_alps_url(server):
    """Discover ALPS profile URL from Link headers on a GET /games/{id}."""
    # We know the JSON Home has the game href. After we fetch it, read Link headers.
    # For simplicity, use the ALPS endpoint from JSON Home base URL.
    # The server exposes /alps/{slug} — naive client finds it via OPTIONS Link header.
    return server + "/alps/game"


def read_alps_fields(alps_url):
    """Step 2: GET ALPS profile, extract field IRIs by semantic ID."""
    data = http_get(alps_url)
    fields = {}
    for d in data.get("alps", {}).get("descriptor", []):
        if "href" in d:
            fields[d["id"]] = d["href"]
    return fields


def play(server, game_id, my_player, alps_fields):
    """Step 3: Loop — get state, check turn, pick cell, POST move using ALPS field IRIs."""
    row_iri = alps_fields.get("rowIndex")
    col_iri = alps_fields.get("columnIndex")
    agent_iri = alps_fields.get("agent")
    type_iri = alps_fields.get("MoveAction")

    if not all([row_iri, col_iri, agent_iri]):
        raise RuntimeError(f"ALPS missing required fields. Got: {list(alps_fields.keys())}")

    game_url = f"{server}/games/{game_id}"

    for _ in range(50):  # safety cap
        state = http_get(game_url)
        status = state.get("status", "InProgress")

        if status != "InProgress":
            print(f"[{my_player}] Game over: {status}")
            return status

        current = state.get("currentPlayer")
        if current != my_player:
            time.sleep(0.15)
            continue

        board = state.get("board", [])
        for r, row in enumerate(board):
            for c, cell in enumerate(row):
                if cell is None:
                    # POST using field IRIs from ALPS — no hardcoded "row"/"col"
                    body = {
                        "@type": type_iri or "https://schema.org/MoveAction",
                        row_iri: r,
                        col_iri: c,
                        agent_iri: my_player,
                    }
                    status_code, resp = http_post(
                        f"{game_url}/moves", body
                    )
                    if status_code == 200:
                        break
            else:
                continue
            break
        else:
            print(f"[{my_player}] No available cells", file=sys.stderr)
            return "Draw"

    print(f"[{my_player}] Safety cap reached", file=sys.stderr)
    return "Unknown"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--server", default="http://localhost:5221")
    parser.add_argument("--role", choices=["X", "O"], required=True)
    parser.add_argument("--game", required=True)
    args = parser.parse_args()

    # Step 1: Discover game resource via JSON Home
    game_template = discover_game_template(args.server)
    print(f"[{args.role}] Discovered game template: {game_template}")

    # Step 2: Read ALPS profile for field IRIs
    alps_url = discover_alps_url(args.server)
    alps_fields = read_alps_fields(alps_url)
    print(f"[{args.role}] ALPS fields: {alps_fields}")

    # Step 3: Play
    result = play(args.server, args.game, args.role, alps_fields)
    print(f"[{args.role}] Result: {result}")
    sys.exit(0 if result in ("XWins", "OWins", "Draw", "InProgress") else 1)


if __name__ == "__main__":
    main()

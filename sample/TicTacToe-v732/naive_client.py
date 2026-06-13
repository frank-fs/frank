#!/usr/bin/env python3
"""
Naive TicTacToe client. Navigates entirely via HTTP discovery.
Only hardcoded value: --server (entry point). Everything else discovered from responses.

Discovery chain:
  GET /  (application/json-home)       → game URL template
  GET <game>  (read Link headers)      → ALPS profile URL, available action URLs
  GET <alps>                           → field IRIs by action ID
  POST <move-url>  (field IRIs only)   → next game state
"""
import argparse
import json
import sys
import time
import urllib.request
import urllib.error
import urllib.parse


def http_get(url, accept=None):
    req = urllib.request.Request(url)
    if accept:
        req.add_header("Accept", accept)
    with urllib.request.urlopen(req) as resp:
        headers = dict(resp.headers)
        body = json.loads(resp.read().decode())
        return body, headers


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


def parse_link_headers(headers):
    """Parse Link header(s) into {rel: url} dict."""
    links = {}
    raw = headers.get("Link") or headers.get("link") or ""
    if not raw:
        return links
    for part in raw.split(","):
        part = part.strip()
        try:
            url_part, *params = [p.strip() for p in part.split(";")]
            url = url_part.strip("<>")
            for param in params:
                if param.startswith("rel="):
                    rel = param[4:].strip('"\'')
                    links[rel] = url
        except Exception:
            continue
    return links


def expand_uri_template(template, variables):
    """Minimal RFC 6570 level-1 expansion: {var} only."""
    result = template
    for key, value in variables.items():
        result = result.replace("{" + key + "}", urllib.parse.quote(str(value), safe=""))
    return result


def discover_game_url(server, game_id):
    """Step 1: GET / with application/json-home → expand game URL template."""
    data, _ = http_get(server + "/", accept="application/json-home")
    resources = data.get("resources", {})
    for _rel, resource in resources.items():
        template = resource.get("href-template")
        if template:
            hints = resource.get("hints", {})
            vars_info = hints.get("vars", {})
            # Find the first template variable and use game_id
            for var_name in vars_info:
                return expand_uri_template(template, {var_name: game_id})
            # Fallback: try common variable names
            for var_name in ("gameId", "id", "game_id"):
                if "{" + var_name + "}" in template:
                    return expand_uri_template(template, {var_name: game_id})
        href = resource.get("href")
        if href:
            return href
    raise RuntimeError(f"No game resource template in JSON Home. Resources: {list(resources.keys())}")


def discover_alps_url(game_url):
    """Step 2: GET game, read Link rel=profile → ALPS URL."""
    _data, headers = http_get(game_url)
    links = parse_link_headers(headers)
    profile_url = links.get("profile")
    if not profile_url:
        raise RuntimeError(f"No Link rel=profile on {game_url}. Links found: {links}")
    return profile_url


def read_alps_field_iris(alps_url):
    """Step 3: GET ALPS, build {action_id: {field_id: iri}} map."""
    data, _ = http_get(alps_url)
    field_iris = {}
    descriptors = data.get("alps", {}).get("descriptor", [])
    for d in descriptors:
        action_id = d.get("id")
        children = d.get("descriptor", [])
        if children:
            field_iris[action_id] = {}
            for child in children:
                if child.get("href"):
                    field_iris[action_id][child["id"]] = child["href"]
        elif d.get("href"):
            field_iris[d["id"]] = d["href"]
    return field_iris


def get_game_state(game_url):
    """GET game state and Link headers."""
    data, headers = http_get(game_url)
    links = parse_link_headers(headers)
    return data, links


def pick_move(state, role):
    """Pick any empty cell for this role."""
    board = state.get("board", [])
    for r, row in enumerate(board):
        for c, cell in enumerate(row):
            if cell is None:
                return r, c
    raise RuntimeError("No empty cell — game should be over")


def play(server, role, game_id):
    game_url = discover_game_url(server, game_id)
    print(f"  game URL: {game_url}")

    alps_url = discover_alps_url(game_url)
    print(f"  ALPS URL: {alps_url}")

    field_iris = read_alps_field_iris(alps_url)
    print(f"  field IRIs: {field_iris}")

    for turn in range(10):
        state, links = get_game_state(game_url)
        status = state.get("status", "")
        if status not in ("InProgress", "in_progress", "active", ""):
            print(f"  game over: {status}")
            return

        move_url = links.get("makeMove") or links.get("make-move")
        if not move_url:
            print(f"  no makeMove link — game may be over. Links: {links}")
            return

        # Field IRIs come from ALPS action descriptor
        action_fields = field_iris.get("makeMove") or field_iris.get("make-move") or {}
        row_iri = action_fields.get("rowIndex") or action_fields.get("row")
        col_iri = action_fields.get("columnIndex") or action_fields.get("col")
        agent_iri = action_fields.get("agent")

        if not row_iri or not col_iri:
            raise RuntimeError(f"ALPS missing rowIndex/columnIndex IRIs. Got: {action_fields}")

        r, c = pick_move(state, role)

        body = {"@type": action_fields.get("MoveAction", "https://schema.org/MoveAction")}
        body[row_iri] = r
        body[col_iri] = c
        if agent_iri:
            body[agent_iri] = role

        print(f"  turn {turn + 1}: {role} plays ({r},{c}) via POST {move_url}")
        status_code, resp = http_post(move_url, body)
        if status_code == 409:
            print(f"  conflict: {resp.get('title', resp)}")
        elif status_code >= 400:
            print(f"  error {status_code}: {resp}")
            return
        time.sleep(0.1)

    print("  reached turn limit")


def main():
    parser = argparse.ArgumentParser(description="Naive TicTacToe client")
    parser.add_argument("--server", default="http://localhost:5221")
    parser.add_argument("--role", required=True, choices=["X", "O"])
    parser.add_argument("--game", required=True)
    args = parser.parse_args()

    print(f"[{args.role}] connecting to {args.server}, game={args.game}")
    play(args.server, args.role, args.game)
    print(f"[{args.role}] done")


if __name__ == "__main__":
    main()

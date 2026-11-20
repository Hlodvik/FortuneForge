"""Disposable loopback HTTP fixture for validating the security scanner runners."""

import argparse
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlsplit


SCHEMA_PATH = "/openapi/v1.json"
KNOWN_PATHS = frozenset({"/", SCHEMA_PATH})
SUPPORTED_METHODS = ("GET", "HEAD", "OPTIONS")
ALLOW_HEADER = ", ".join(SUPPORTED_METHODS)
HOME_DOCUMENT = {"status": "ok", "fixture": "fortune-forge-security"}
OPENAPI_DOCUMENT = {
    "openapi": "3.0.3",
    "info": {"title": "Fortune Forge scanner fixture", "version": "1.0.0"},
    "paths": {
        "/": {
            "get": {
                "operationId": "getFixtureStatus",
                "responses": {
                    "200": {
                        "description": "Deterministic fixture status",
                        "content": {
                            "application/json": {
                                "schema": {
                                    "type": "object",
                                    "required": list(HOME_DOCUMENT),
                                    "properties": {
                                        name: {"type": "string", "enum": [value]}
                                        for name, value in HOME_DOCUMENT.items()
                                    },
                                    "additionalProperties": False,
                                }
                            }
                        },
                    }
                },
            },
            "head": {
                "operationId": "getFixtureHeaders",
                "responses": {"200": {"description": "GET headers without a body"}},
            },
            "options": {
                "operationId": "getFixtureMethods",
                "responses": {
                    "204": {
                        "description": "Supported methods",
                        "headers": {
                            "Allow": {
                                "required": True,
                                "schema": {"type": "string", "enum": [ALLOW_HEADER]},
                            }
                        },
                    }
                },
            },
        }
    },
}


class FixtureRequestHandler(BaseHTTPRequestHandler):
    """Serve explicit in-memory routes; never expose the filesystem."""

    def log_message(self, _format, *args):
        """Keep requests and credentials out of fixture logs."""

    def _known_path(self):
        return urlsplit(self.path).path in KNOWN_PATHS

    def _send_json(self, status, document, *, allow=False):
        body = json.dumps(document, separators=(",", ":")).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        if allow:
            self.send_header("Allow", ALLOW_HEADER)
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(body)

    def _not_found(self):
        self._send_json(404, {"error": "not_found"})

    def do_GET(self):
        path = urlsplit(self.path).path
        if path == "/":
            self._send_json(200, HOME_DOCUMENT)
        elif path == SCHEMA_PATH:
            self._send_json(200, OPENAPI_DOCUMENT)
        else:
            self._not_found()

    def do_HEAD(self):
        self.do_GET()

    def do_OPTIONS(self):
        if not self._known_path():
            self._not_found()
            return
        self.send_response(204)
        self.send_header("Allow", ALLOW_HEADER)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def _unsupported_method(self):
        if self._known_path():
            self._send_json(405, {"error": "method_not_allowed"}, allow=True)
        else:
            self._not_found()

    def __getattr__(self, name):
        # BaseHTTPRequestHandler otherwise returns 501 for unimplemented verbs.
        if name.startswith("do_"):
            return self._unsupported_method
        raise AttributeError(name)


def create_server(port=0):
    """Create an ephemeral, local-only server for CLI use or method tests."""
    if not 0 <= port <= 65535:
        raise ValueError("port must be between 0 and 65535")
    return ThreadingHTTPServer(("127.0.0.1", port), FixtureRequestHandler)


def parse_port(value):
    port = int(value)
    if not 0 <= port <= 65535:
        raise argparse.ArgumentTypeError("port must be between 0 and 65535")
    return port


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--port", type=parse_port, default=0,
        help="loopback port; 0 chooses a free ephemeral port (default: 0)",
    )
    args = parser.parse_args()
    with create_server(args.port) as server:
        host, port = server.server_address
        print(f"http://{host}:{port}", flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()

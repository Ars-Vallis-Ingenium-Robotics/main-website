#!/usr/bin/env python3
"""Static preview server for the AVI Robotics landing page branch."""
from http.server import ThreadingHTTPServer, SimpleHTTPRequestHandler
from pathlib import Path
import functools

ROOT = Path(__file__).resolve().parents[1]
HOST = "0.0.0.0"
PORT = 8025

class Handler(SimpleHTTPRequestHandler):
    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

if __name__ == "__main__":
    handler = functools.partial(Handler, directory=str(ROOT))
    server = ThreadingHTTPServer((HOST, PORT), handler)
    print(f"Serving {ROOT} on http://{HOST}:{PORT}", flush=True)
    server.serve_forever()

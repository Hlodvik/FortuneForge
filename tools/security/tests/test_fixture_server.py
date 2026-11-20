"""HTTP contract checks for the disposable scanner fixture."""

import http.client
import json
import threading
import unittest

from fixture_server import ALLOW_HEADER, HOME_DOCUMENT, SCHEMA_PATH, create_server


class FixtureServerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = create_server()
        cls.thread = threading.Thread(target=cls.server.serve_forever, daemon=True)
        cls.thread.start()

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()
        cls.server.server_close()
        cls.thread.join(timeout=5)

    def request(self, method, path):
        connection = http.client.HTTPConnection(*self.server.server_address, timeout=5)
        try:
            connection.request(method, path)
            response = connection.getresponse()
            return response.status, dict(response.getheaders()), response.read()
        finally:
            connection.close()

    def test_loopback_binding_and_documented_response(self):
        self.assertEqual(self.server.server_address[0], "127.0.0.1")
        status, headers, body = self.request("GET", "/")
        self.assertEqual(status, 200)
        self.assertEqual(headers["Content-Type"], "application/json")
        self.assertEqual(int(headers["Content-Length"]), len(body))
        self.assertEqual(json.loads(body), HOME_DOCUMENT)
        status, _, body = self.request("GET", SCHEMA_PATH)
        self.assertEqual(status, 200)
        spec = json.loads(body)
        self.assertEqual(set(spec["paths"]["/"]), {"get", "head", "options"})

    def test_head_and_options_are_truthfully_advertised(self):
        for path in ("/", SCHEMA_PATH):
            with self.subTest(path=path):
                status, headers, body = self.request("HEAD", path)
                self.assertEqual(status, 200)
                self.assertGreater(int(headers["Content-Length"]), 0)
                self.assertEqual(body, b"")
                status, headers, body = self.request("OPTIONS", path)
                self.assertEqual(status, 204)
                self.assertEqual(headers["Allow"], ALLOW_HEADER)
                self.assertEqual(body, b"")

    def test_unsupported_methods_return_405_including_trace(self):
        for path in ("/", SCHEMA_PATH):
            for method in ("TRACE", "CONNECT", "POST", "PUT", "PATCH", "DELETE", "BREW"):
                with self.subTest(path=path, method=method):
                    status, headers, body = self.request(method, path)
                    self.assertEqual(status, 405)
                    self.assertEqual(headers["Allow"], ALLOW_HEADER)
                    self.assertEqual(json.loads(body), {"error": "method_not_allowed"})

    def test_unknown_routes_never_serve_files_or_directory_listings(self):
        for path in ("/missing", "/README.md", "/../fixture_server.py"):
            for method in ("GET", "HEAD", "OPTIONS", "TRACE"):
                with self.subTest(path=path, method=method):
                    status, headers, body = self.request(method, path)
                    self.assertEqual(status, 404)
                    self.assertNotIn("Allow", headers)
                    if method != "HEAD":
                        self.assertEqual(json.loads(body), {"error": "not_found"})


if __name__ == "__main__":
    unittest.main()

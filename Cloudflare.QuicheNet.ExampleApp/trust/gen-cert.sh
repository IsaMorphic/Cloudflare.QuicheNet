#!/bin/bash
openssl req -x509 -nodes -days 365 -newkey rsa:2048 -keyout key.pem -out cert.pem -addext "subjectAltName = DNS:localhost, IP:127.0.0.1"
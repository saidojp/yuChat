#!/bin/sh
# Heroku release phase entry point. Heroku wraps the release command in `sh -c`, so the
# absolute path is used and no extra shell layer is needed. Runs EF Core migrations only.
set -e
cd /app
exec dotnet /app/YuChat.Api.dll --migrate

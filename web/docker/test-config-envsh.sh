#!/bin/bash
# Prueba de regresión de 15-impifact-config.envsh sobre la imagen del panel: cada valor inválido (incluidos los que
# traen un salto de línea, que partían el encabezado de la CSP o el JSON de config.json) detiene el contenedor con el
# mensaje del script, y un valor válido arranca nginx. Desde la raíz del repositorio, con la imagen construida:
#   docker build -f Dockerfile.web -t impifact-web:local web
#   bash web/docker/test-config-envsh.sh [imagen]
set -u
IMAGE="${1:-impifact-web:local}"
NL=$'\n'
failures=0

# Un valor inválido: el contenedor termina con código distinto de 0 y escribe el mensaje de 15-impifact-config.envsh.
# Si sigue corriendo a los 15 s, nginx arrancó con el valor: cuenta como no rechazado.
expect_rejected() {
    local name="$1" variable="$2" value="$3" container output status="running"
    container=$(docker run -d -e "$variable=$value" "$IMAGE")
    for _ in $(seq 1 15); do
        if [ "$(docker inspect -f '{{.State.Running}}' "$container")" = "false" ]; then
            status=$(docker inspect -f '{{.State.ExitCode}}' "$container")
            break
        fi
        sleep 1
    done
    output=$(docker logs "$container" 2>&1)
    docker rm -f "$container" >/dev/null
    if [ "$status" != "running" ] && [ "$status" -ne 0 ] && grep -q '^15-impifact-config.envsh: ' <<<"$output"; then
        echo "PASS rejected: $name (exit $status): $(grep '^15-impifact-config.envsh: ' <<<"$output" | head -1)"
    else
        echo "FAIL not rejected: $name (exit $status)"
        failures=$((failures + 1))
    fi
}

expect_rejected "API URL with a second line" IMPIFACT_API_BASE_URL "https://api.example.com${NL}https://evil.example"
expect_rejected "API URL with a trailing line break" IMPIFACT_API_BASE_URL "https://api.example.com${NL}"
expect_rejected "warn minutes with injected JSON" IMPIFACT_INACTIVITY_WARN_MINUTES "25${NL},\"evil\":1"
expect_rejected "logout minutes with a carriage return" IMPIFACT_INACTIVITY_LOGOUT_MINUTES $'30\r'
expect_rejected "plain http to another host" IMPIFACT_API_BASE_URL "http://api.example.com"
expect_rejected "warn minutes not lower than logout" IMPIFACT_INACTIVITY_WARN_MINUTES "30"

# Un valor válido: nginx arranca y config.json trae exactamente esa URL.
container=$(docker run -d -e IMPIFACT_API_BASE_URL=https://api.example.com/ "$IMAGE")
config=""
for _ in $(seq 1 20); do
    config=$(docker exec "$container" wget -q -O - http://127.0.0.1:8080/config.json 2>/dev/null) && break
    sleep 1
done
expected='{"apiBaseUrl":"https://api.example.com","inactivity":{"warnMinutes":25,"logoutMinutes":30},"features":{}}'
if [ "$config" = "$expected" ]; then
    echo "PASS valid value runs: config.json = $config"
else
    echo "FAIL valid value: config.json = ${config:-<no answer>}"
    docker logs "$container" 2>&1 | tail -5
    failures=$((failures + 1))
fi
docker rm -f "$container" >/dev/null

echo "$failures failure(s)"
[ "$failures" -eq 0 ]

#!/usr/bin/env bash
# =============================================================================
# post-scaffold.sh — Leccionario e Inspección (cplec)
#
# Post-procesa el resultado de `dotnet ef dbcontext scaffold` (o de EF Power
# Tools) para que compile bajo <Nullable>enable</Nullable> +
# <TreatWarningsAsErrors>true</TreatWarningsAsErrors>.
#
# Por qué hace falta:
#   .editorconfig marca `[**/Domain/Entities/*.cs]` como `generated_code = true`
#   para que las reglas de estilo y los analizadores no se apliquen sobre las
#   entidades. Pero esto también obliga al compilador a exigir un `#nullable
#   enable` explícito en esos archivos (warning CS8669 → error). El tooling
#   de EF 8.0.x NO lo emite en los archivos generados, así que lo agregamos
#   acá.
#
# Uso:
#   scripts/post-scaffold.sh        # procesa src/Leccionario.Api/Domain/Entities
#
# Idempotente: si el archivo ya tiene la directiva, no la duplica.
# =============================================================================
set -euo pipefail

raiz=$(git rev-parse --show-toplevel 2>/dev/null || pwd)
carpeta="$raiz/src/Leccionario.Api/Domain/Entities"

[[ -d "$carpeta" ]] || { echo "No existe $carpeta"; exit 1; }

shopt -s nullglob
archivos=("$carpeta"/*.cs)
shopt -u nullglob

[[ ${#archivos[@]} -eq 0 ]] && { echo "No hay archivos en $carpeta"; exit 0; }

modificados=0
for f in "${archivos[@]}"; do
    necesita_nullable=0
    necesita_pragma=0
    if ! head -10 "$f" | grep -q '^#nullable enable'; then
        necesita_nullable=1
    fi
    if ! head -10 "$f" | grep -q '^#pragma warning disable CS8981'; then
        necesita_pragma=1
    fi
    [[ $necesita_nullable -eq 0 && $necesita_pragma -eq 0 ]] && continue

    if [[ $necesita_nullable -eq 1 ]]; then
        # Insertar `#nullable enable` después de los usings (o al principio
        # si no hay usings).
        awk '
            BEGIN { done = 0 }
            /^using / { print; next }
            { if (!done) { print "#nullable enable"; done = 1 } print }
            END { if (!done) { print "#nullable enable" } }
        ' "$f" > "$f.tmp" && mv "$f.tmp" "$f"
    fi

    if [[ $necesita_pragma -eq 1 ]]; then
        # Insertar `#pragma warning disable CS8981` justo después de la
        # directiva de nulabilidad. Las clases de entidad se llaman como la
        # tabla (`usuarios`, `periodos`, …) para que el mapping con EF
        # Core salga tal cual; eso choca con CS8981 (nombre reservado).
        awk '
            BEGIN { done = 0 }
            /^#nullable enable/ { print; print "#pragma warning disable CS8981"; done = 1; next }
            /^using / && done == 0 { print; next }
            { if (!done) { print "#nullable enable"; print "#pragma warning disable CS8981"; done = 1 } print }
            END { if (!done) { print "#nullable enable"; print "#pragma warning disable CS8981" } }
        ' "$f" > "$f.tmp" && mv "$f.tmp" "$f"
    fi

    modificados=$((modificados + 1))
done

echo "post-scaffold: $modificados archivos actualizados en Domain/Entities/"

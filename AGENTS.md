# Mars Trainer Global Rules

PROJECT: Mars outpost survival trainer. Unity, URP, C#. Survive 1 Mars year (668.6 sols), time fast-forwarded.
ARCH: Pure C# sim (no UnityEngine in Assets/Scripts/Sim). MonoBehaviours only render/input. Sim advanced by SimClock. Events via C# event bus. All tunables in GameConstants.cs, JSON or ScriptableObjects. No magic numbers.
STYLE: low-poly, flat-shaded, warm sunset palette, matte, no image textures, one low directional light.
CODE: small files, one class per file, namespace MarsTrainer.<Area>. Asmdef per area. No singletons except GameServices. No LINQ or allocations in Update.
DATA: no runtime network. JSON in StreamingAssets, read via UnityWebRequest (WebGL-safe). Fail soft: defaults + warning.
TESTS: sim logic gets EditMode tests. Run them before finishing.
SCOPE: do only the prompt scope. No unrelated refactors. Open only listed files; do not scan the repo.
OUTPUT: terse. Final reply max 5 lines: what changed, files, test result, open issues. No code echo.
GIT: never add Library/. Do not commit; I commit.

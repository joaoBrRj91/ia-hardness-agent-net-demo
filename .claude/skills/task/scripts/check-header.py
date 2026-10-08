import json, re, sys

PRIORITY = ["needs-answers", "needs-discussion", "needs-split", "ready-for-review"]

text = open(sys.argv[1], encoding="utf-8").read()
m = re.match(r"^---\n(.*?)\n---", text, re.S)
if not m:
    print(json.dumps({"verdict": "invalid", "error": "cabeçalho ausente"}))
    sys.exit(1)

h = {}
for line in m.group(1).splitlines():
    key, _, value = line.partition(":")
    h[key.strip()] = value.split("#")[0].strip()

# verdict exigido pelos campos, do menos para o mais restritivo
expected = "ready-for-review"
if h.get("complexity") == "G":
    expected = "needs-split"
if h.get("structuralDecision") == "true":
    expected = "needs-discussion"
if int(h.get("blockingQuestions") or 0) > 0:
    expected = "needs-answers"

declared = h.get("verdict")
verdict = expected
if declared in PRIORITY:
    verdict = min(declared, expected, key=PRIORITY.index)

plan_ok = (h.get("planWritten") == "true") == (verdict == "ready-for-review")
print(json.dumps({
    "verdict": verdict,
    "declared": declared,
    "consistent": declared == verdict and plan_ok,
}))
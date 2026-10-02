# OPEN QUESTIONS

Questions where there is no certainty about the Creatio, clio or Oracle API.
Such places in code are marked `// TODO(verify): ...` (`-- TODO(verify): ...` in SQL).

## Template

```
## Q-NNN: <question>
- Date: YYYY-MM-DD
- Where: <file / component>
- Context: what we are trying to do and what is in doubt
- Status: open | closed (answer, source)
```

---

## Q-001: Error code for invalid rate in CALC_ANNUITY
- Date: 2026-10-02
- Where: oracle/init/04_pkg_loan_scoring.pkb (`C_ERR_INTERNAL`)
- Context: CALC_ANNUITY must reject rate <= 0 (division by zero). The contract defines only
  -20001..-20005, none of which means "invalid rate". Currently -20000 is raised; the API would map
  it to 500 INTERNAL_ERROR, which is correct because user input cannot produce it. Add -20000
  (or another code) to CONTRACTS.md section 6?
- Status: open

## Q-002: Is basePayment rounded before computing DTI?
- Date: 2026-10-02
- Where: CONTRACTS.md section 5, step 1–2; all four implementations
- Context: "Money rounding — to 2 decimals" may or may not apply to basePayment. Near DTI = 0.30 / 0.50
  Oracle NUMBER, C# decimal and JS double can disagree when unrounded values are compared.
  Rounding basePayment to 2 decimals would make the comparison deterministic.
- Status: open (current: not rounded, ADR-005)

## Q-003: Lower bound for maxApprovedAmount
- Date: 2026-10-02
- Where: CONTRACTS.md section 5, step 7
- Context: for REVIEW/APPROVE the value can be below 50 000 (the minimum application amount).
  Keep as is, clamp to 50 000, or turn such cases into REJECT?
- Status: open (current: no lower bound, ADR-005)

## Q-004: Example numbers in CONTRACTS.md sections 3–4 do not match the algorithm
- Date: 2026-10-02
- Where: CONTRACTS.md sections 3 and 4
- Context: for the example input (500 000 / 24 / 120 000 / CONSUMER) the algorithm gives score 800,
  rate 19.90, payment 25 423.48, max amount 944 000 — not 790 / 20.90 / 25 740.12 / 940 000.
  The examples are marked illustrative; should they be replaced with the real values?
- Status: open

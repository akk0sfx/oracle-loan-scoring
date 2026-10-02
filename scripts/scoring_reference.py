#!/usr/bin/env python3
"""Independent reference scoring calculation per docs/CONTRACTS.md, section 5.

Produces expected numbers for the PL/SQL tests (and later C#/JS) without peeking at the
implementation under test. Decimal with 40 significant digits is close to Oracle NUMBER precision.

Run: python3 scripts/scoring_reference.py
"""
from decimal import ROUND_FLOOR, ROUND_HALF_UP, Decimal, getcontext

getcontext().prec = 40

BASE_RATE = {
    "CONSUMER": Decimal("21.90"),
    "CAR": Decimal("17.90"),
    "MORTGAGE": Decimal("14.50"),
    "REFINANCE": Decimal("19.50"),
    "OTHER": Decimal("24.90"),
}
PURPOSE_BONUS = {"MORTGAGE": 50, "CAR": 30, "REFINANCE": 10, "CONSUMER": 0, "OTHER": -30}


def money(x: Decimal) -> Decimal:
    # Half away from zero: for positive numbers ROUND_HALF_UP matches Oracle ROUND.
    return x.quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)


def annuity(amount: Decimal, rate: Decimal, term: int) -> Decimal:
    r = rate / 12 / 100
    return amount * r / (1 - (1 + r) ** -term)


def evaluate(amount: str, term: int, income: str, purpose: str) -> dict:
    a, inc = Decimal(amount), Decimal(income)
    base_payment = annuity(a, Decimal("20.00"), term)
    dti = base_payment / inc

    score, reasons = 600, []
    if dti < Decimal("0.30"):
        score += 200; reasons.append("DTI_LOW")
    elif dti <= Decimal("0.50"):
        score += 50; reasons.append("DTI_MEDIUM")
    else:
        score -= 250; reasons.append("DTI_HIGH")
    if term > 60:
        score -= 50; reasons.append("LONG_TERM")
    if a > 3_000_000:
        score -= 50; reasons.append("LARGE_AMOUNT")
    score += PURPOSE_BONUS[purpose]; reasons.append(f"PURPOSE_{purpose}")
    score = max(0, min(1000, score))

    decision = "APPROVE" if score >= 700 else "REVIEW" if score >= 500 else "REJECT"
    result = {"base_payment": base_payment, "dti": dti, "score": score,
              "decision": decision, "reasons": ",".join(reasons)}
    if decision == "REJECT":
        return result | {"rate": None, "payment": None, "max_amount": Decimal(0)}

    discount = Decimal("2.00") if score >= 800 else Decimal("1.00") if score >= 700 else Decimal(0)
    rate = BASE_RATE[purpose] - discount
    raw_payment = annuity(a, rate, term)
    r = rate / 12 / 100
    raw_max = Decimal("0.40") * inc * (1 - (1 + r) ** -term) / r
    max_amount = min((raw_max / 1000).to_integral_value(ROUND_FLOOR) * 1000, Decimal(5_000_000))
    return result | {"rate": rate, "raw_payment": raw_payment, "payment": money(raw_payment),
                     "raw_max": raw_max, "max_amount": max_amount}


CASES = {
    "annuity 100000 @ 12% x 12": None,
    "APPROVE": ("500000", 24, "120000", "CONSUMER"),
    "REVIEW": ("1000000", 36, "100000", "CAR"),
    "REJECT": ("2000000", 24, "100000", "OTHER"),
    "LONG_TERM + LARGE_AMOUNT": ("4000000", 72, "400000", "MORTGAGE"),
    "min score (220)": ("5000000", 84, "1000", "OTHER"),
    "max score (850)": ("100000", 12, "1000000", "MORTGAGE"),
}

if __name__ == "__main__":
    a = annuity(Decimal(100000), Decimal(12), 12)
    print(f"annuity(100000, 12, 12) = {a}  -> round 2: {money(a)}")
    for name, args in CASES.items():
        if args is None:
            continue
        print(f"\n== {name}: amount={args[0]} term={args[1]} income={args[2]} purpose={args[3]}")
        for k, v in evaluate(*args).items():
            print(f"   {k:13} = {v}")

#!/usr/bin/env python3
"""Preview or import bundled MRL reference snapshots (GL-A-3 / GL-A-4)."""
import argparse
import http.client
import csv
import hashlib
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime
from decimal import Decimal, InvalidOperation
from pathlib import Path

ROOT = Path(__file__).resolve().parent / "catalog-reference-data"
SUBSTANCES_FILE = ROOT / "pesticide_substances-v1.1.csv"
MRL_FILE = ROOT / "mrl_table-v1.1.csv"
SUBSTANCES_URL = "https://huubng.youtrack.cloud/articles/GL-A-4"
MRL_URL = "https://huubng.youtrack.cloud/articles/GL-A-3"
VERSION = "1.1"
LEGAL_REFERENCE = "Thông tư 50/2016/TT-BYT"
EFFECTIVE_DATE = "2017-07-01"
SOURCE_VERSION = "VN_TT50_2016"
SUBSTANCE_CODES = {"TCZ001", "IPT002", "HXC003", "PPC004", "CBD005", "CPF006", "IMD007", "ABA008", "BUP009", "LCH010", "THX011", "FBN012", "PCZ013", "TBZ014"}
RICE_NAME = "Gạo (rice)"
HUSKED_NAME = "Gạo đã xát vỏ (husked rice)"
EXPECTED = {"CBD005": (HUSKED_NAME, "2"), "ABA008": (HUSKED_NAME, "0.1"), "CPF006": (RICE_NAME, "0.5"), "LCH010": (RICE_NAME, "1"), "TBZ014": (RICE_NAME, "1.5")}
MARKET_ALIASES = {"VN": "VIETNAM", "VIETNAM": "VIETNAM", "EU": "EU", "US": "USA", "USA": "USA", "JP": "JAPAN", "JAPAN": "JAPAN", "KR": "KOREA", "KOREA": "KOREA", "CODEX": "CODEX"}
MARKET_SPECS = {
    "VIETNAM": {
        "defaultMarketCode": "VN",
        "marketNames": {"vietnam", "viet nam", "việt nam"},
        "legalReference": LEGAL_REFERENCE,
        "legalVersion": SOURCE_VERSION,
        "defaultEffectiveDate": EFFECTIVE_DATE,
        "commodities": (("rice", {RICE_NAME}), ("husked-rice", {HUSKED_NAME})),
        "excludedCommodities": set(),
    },
    "EU": {
        "defaultMarketCode": "EU",
        "marketNames": {"eu", "european union", "europe", "châu âu", "liên minh châu âu"},
        "legalReference": "Regulation (EC) No 396/2005",
        "legalVersion": "EU_v2026.3",
        "defaultEffectiveDate": None,
        "commodities": (("rice", {"Rice"}),),
        "excludedCommodities": set(),
    },
    "USA": {
        "defaultMarketCode": "US",
        "marketNames": {"us", "usa", "united states", "united states of america", "hoa kỳ", "mỹ"},
        "legalReference": "40 CFR Part 180",
        "legalVersion": "US_40CFR_v2026.09",
        "defaultEffectiveDate": None,
        "commodities": (("rice", {"Rice, grain"}), ("husked-rice", {"Rice, husked"}), ("polished-rice", {"Rice, polished rice"}), ("bran", {"Rice, bran"})),
        "excludedCommodities": set(),
    },
    "JAPAN": {
        "defaultMarketCode": "JP",
        "marketNames": {"jp", "japan", "nhật bản", "nhat ban"},
        "legalReference": "Japan Food Sanitation Act positive list (MHLW / CAA)",
        "legalVersion": "JP_v2026.3",
        "defaultEffectiveDate": None,
        "commodities": (("husked-rice", {"Rice (brown rice / 玄米)"}),),
        "excludedCommodities": set(),
    },
    "KOREA": {
        "defaultMarketCode": "KR",
        "marketNames": {"kr", "korea", "republic of korea", "south korea", "hàn quốc", "han quoc"},
        "legalReference": "MFDS Food Code, Annex 4",
        "legalVersion": "KR_v2026.3",
        "defaultEffectiveDate": None,
        "commodities": (("rice", {"Rice (쌀)"}),),
        "excludedCommodities": set(),
    },
    "CODEX": {
        "defaultMarketCode": "CODEX",
        "marketNames": {"codex", "codex alimentarius"},
        "legalReference": "Codex Alimentarius CXL",
        "legalVersion": "CODEX_CAC48",
        "defaultEffectiveDate": None,
        "commodities": (("husked-rice", {"Rice, husked"}), ("polished-rice", {"Rice, polished"}), ("bran", {"Rice, bran"})),
        "excludedCommodities": {"Rice (paddy)", "Rice straw"},
    },
}


def load_csv(path):
    with path.open(encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream, strict=True)
        if not reader.fieldnames or len(reader.fieldnames) != len(set(reader.fieldnames)):
            raise ValueError(f"{path.name}: missing or duplicate CSV headers")
        rows = list(reader)
        if any(None in row or any(value is None for value in row.values()) for row in rows):
            raise ValueError(f"{path.name}: malformed CSV row width")
        return rows


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def normalize_market(source_market):
    key = (source_market or "VIETNAM").strip().upper()
    if key not in MARKET_ALIASES:
        raise ValueError(f"unsupported source market {source_market!r}; choose from {', '.join(MARKET_SPECS)}")
    return MARKET_ALIASES[key]


def validate_date(value):
    if value is None:
        return None
    text = value.strip()
    try:
        if len(text) != 10 or datetime.strptime(text, "%Y-%m-%d").strftime("%Y-%m-%d") != text:
            raise ValueError
    except ValueError as exc:
        raise ValueError("--effective-from must use YYYY-MM-DD") from exc
    return text


def prepare(substance_rows, mrl_rows, rice_code=None, husked_code=None, source_market="VIETNAM", polished_code=None, bran_code=None):
    market = normalize_market(source_market)
    spec = MARKET_SPECS[market]
    raw_codes = {"rice": rice_code, "husked-rice": husked_code, "polished-rice": polished_code, "bran": bran_code}
    allowed_mappings = {label for label, _ in spec["commodities"]}
    for label, code in raw_codes.items():
        if code is not None and label not in allowed_mappings:
            raise ValueError(f"{market} does not have a {label} MRL group")
    if market == "VIETNAM" and (rice_code is not None or husked_code is not None):
        if not rice_code or not husked_code:
            raise ValueError("both rice and husked-rice commodity codes are required together")
    normalized_codes = {}
    for label, code in raw_codes.items():
        if code is not None:
            clean = code.strip().upper()
            if not clean:
                raise ValueError(f"{label} commodity code must be nonempty")
            if "PADDY" in clean:
                raise ValueError("PADDY commodity codes are not valid MRL mappings")
            normalized_codes[label] = clean
    if len(set(normalized_codes.values())) != len(normalized_codes):
        raise ValueError("mapped commodity codes must be distinct")
    if not {"substance_id", "common_name_en"}.issubset(substance_rows[0] if substance_rows else {}):
        raise ValueError("substance snapshot is missing required columns")
    if {r.get("substance_id", "").strip().upper() for r in substance_rows} != SUBSTANCE_CODES or len(substance_rows) != 14:
        raise ValueError("substance snapshot must contain exactly the 14 expected unique substance codes")
    if not {"mrl_id", "substance_id", "substance_name", "market", "commodity_name_en", "regulation_reference", "mrl_value_mg_kg", "mrl_unit", "effective_date", "version", "status", "notes"}.issubset(mrl_rows[0] if mrl_rows else {}):
        raise ValueError("MRL snapshot is missing required columns")
    if len(mrl_rows) != 96 or len({r["mrl_id"] for r in mrl_rows}) != 96 or any(not r.get("market", "").strip() for r in mrl_rows):
        raise ValueError("MRL snapshot must contain exactly 96 uniquely identified source rows")
    names = {}
    for row in substance_rows:
        code, name = row["substance_id"].strip().upper(), row["common_name_en"].strip()
        if not code or not name or code in names or len(code) > 60:
            raise ValueError(f"invalid or duplicate substance code/name: {code!r}")
        names[code] = name
    market_rows = [r for r in mrl_rows if r.get("market") == market]
    if {r.get("substance_id", "").strip().upper() for r in market_rows} != SUBSTANCE_CODES:
        raise ValueError(f"MRL snapshot {market} rows invalid; expected all 14 unique substances")
    allowed_commodities = {name for _, group_names in spec["commodities"] for name in group_names}
    active, excluded = [], []
    for row in market_rows:
        code, status = row["substance_id"].strip().upper(), row["status"].strip()
        if code not in names or row["substance_name"].strip() != names[code]:
            raise ValueError(f"{market} row has unknown substance or conflicting name: {code}")
        if market == "VIETNAM" and (row["effective_date"] != EFFECTIVE_DATE or row["version"] != SOURCE_VERSION or row["regulation_reference"].find(LEGAL_REFERENCE) < 0):
            raise ValueError(f"Vietnam row has unexpected legal reference/date/version: {code}")
        if status in {"NO_MRL", "DEFAULT_APPLIES", "ACTIVE_TIME_LIMITED"}:
            if status == "NO_MRL" and (row["mrl_value_mg_kg"].strip() or not row["notes"].strip()):
                raise ValueError(f"NO_MRL must have source note and no numeric value: {code}")
            if status in {"DEFAULT_APPLIES", "ACTIVE_TIME_LIMITED"} and (not row["mrl_value_mg_kg"].strip() or not row["notes"].strip()):
                raise ValueError(f"{status} must have source value and note: {code}")
            excluded.append(row)
        elif status == "ACTIVE":
            try:
                value = Decimal(row["mrl_value_mg_kg"])
            except InvalidOperation as exc:
                raise ValueError(f"invalid numeric limit for {code}") from exc
            if not value.is_finite() or value < 0 or row["mrl_unit"].strip().lower() != "mg/kg":
                raise ValueError(f"invalid active MRL value/unit for {code}")
            if row["commodity_name_en"] in spec["excludedCommodities"]:
                excluded.append(row)
                continue
            if row["commodity_name_en"] not in allowed_commodities:
                raise ValueError(f"unexpected {market} commodity: {row['commodity_name_en']}")
            active.append(row)
        else:
            raise ValueError(f"unsupported {market} status {status!r} for {code}")
    if market == "VIETNAM":
        if len(active) != 5 or len(excluded) != 9:
            raise ValueError(f"expected 5 numeric active rows and 9 NO_MRL rows; found {len(active)} and {len(excluded)}")
        for code, (commodity, value) in EXPECTED.items():
            row = next((r for r in active if r["substance_id"] == code), None)
            if not row or row["commodity_name_en"] != commodity or row["mrl_value_mg_kg"].strip() != value:
                raise ValueError(f"unexpected source value or commodity for {code}")
    drafts = []
    for label, commodity_names in spec["commodities"]:
        rows = [r for r in active if r["commodity_name_en"] in commodity_names]
        if len({r["substance_id"].strip().upper() for r in rows}) != len(rows):
            raise ValueError(f"duplicate substance in {market} {label} group")
        mapped = normalized_codes.get(label)
        if rows and (not normalized_codes or mapped is not None):
            drafts.append({"mapping": label, "commodityCode": mapped, "rows": rows})
    return names, active, excluded, drafts


def source_ref(source_market="VIETNAM", effective_date=None):
    market = normalize_market(source_market)
    spec = MARKET_SPECS[market]
    resolved_date = validate_date(effective_date) or spec["defaultEffectiveDate"]
    payload = {"articles": [{"url": SUBSTANCES_URL, "version": VERSION}, {"url": MRL_URL, "version": VERSION}], "legalReference": spec["legalReference"], "legalVersion": spec["legalVersion"], "effectiveDate": resolved_date, "snapshot": [{"path": f"tools/catalog-reference-data/{SUBSTANCES_FILE.name}", "sha256": sha256(SUBSTANCES_FILE)}, {"path": f"tools/catalog-reference-data/{MRL_FILE.name}", "sha256": sha256(MRL_FILE)}], "semantics": "Historical source snapshot, not current legal verification. NO_MRL means no numeric limit established in this dataset, not zero or unlimited; CAS data retained in CSV, not imported."}
    if market != "VIETNAM":
        payload["sourceMarket"] = market
    return json.dumps(payload, ensure_ascii=False, separators=(",", ":"))

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise urllib.error.HTTPError(req.full_url, code, "redirect refused", headers, fp)


class Api:
    def __init__(self, base, token):
        if not token or not token.isascii() or any(ch.isspace() or ord(ch) < 32 or ord(ch) == 127 for ch in token):
            raise ValueError("GRAINLEDGER_IMPORT_TOKEN must be nonempty ASCII without whitespace or control characters")
        parsed = urllib.parse.urlsplit(base)
        if parsed.username or parsed.password or parsed.query or parsed.fragment or not parsed.hostname:
            raise ValueError("--base-url must not contain credentials, query, or fragment")
        if parsed.scheme != "https" and not (parsed.scheme == "http" and parsed.hostname in {"localhost", "127.0.0.1", "::1"}):
            raise ValueError("--base-url must use HTTPS; HTTP allowed only for loopback")
        self.base = base.rstrip("/")
        self.opener = urllib.request.build_opener(NoRedirect)
        self.token = token

    def request(self, method, path, body=None):
        data = None if body is None else json.dumps(body, ensure_ascii=False).encode()
        request = urllib.request.Request(self.base + path, data=data, method=method, headers={"Authorization": "Bearer " + self.token, "Accept": "application/json", "Content-Type": "application/json"})
        try:
            with self.opener.open(request, timeout=30) as response:
                return json.loads(response.read(), parse_float=Decimal) if response.status != 204 else None
        except urllib.error.HTTPError as exc:
            raise RuntimeError(f"{method} {path} failed HTTP {exc.code}") from None
        except urllib.error.URLError as exc:
            raise RuntimeError(f"{method} {path} failed: {exc.reason}") from None
        except (OSError, http.client.HTTPException) as exc:
            raise RuntimeError(f"{method} {path} failed: {type(exc).__name__}") from None

    def listing(self, path):
        result, page = [], 1
        while True:
            payload = self.request("GET", f"{path}?page={page}&pageSize=100")
            result.extend(payload.get("items", []))
            if not payload.get("hasMore"):
                return result
            page += 1


def exact_code(items, code):
    return [item for item in items if item.get("code", "").casefold() == code.casefold()]


def preflight(api, names, drafts, market_code, source_market="VIETNAM", effective_date=None):
    market = normalize_market(source_market)
    spec = MARKET_SPECS[market]
    resolved_date = validate_date(effective_date) or spec["defaultEffectiveDate"]
    if not resolved_date:
        raise ValueError(f"--effective-from YYYY-MM-DD is required when applying {market} MRL drafts")
    substances = api.listing("/api/active-substances")
    existing_by_code = {}
    for code, name in names.items():
        matches = exact_code(substances, code)
        name_matches = [x for x in substances if x.get("name", "").casefold() == name.casefold()]
        if len(matches) > 1 or len(name_matches) > 1 or (matches and matches[0]["name"].casefold() != name.casefold()) or (name_matches and name_matches[0]["code"].casefold() != code.casefold()):
            raise ValueError(f"existing substance conflicts with source code/name {code} / {name}")
        existing_by_code[code] = matches[0] if matches else None
    commodities = api.listing("/api/commodities")
    mapped = {}
    for draft in drafts:
        found = exact_code(commodities, draft["commodityCode"])
        if len(found) != 1 or found[0].get("isArchived") is not False:
            raise ValueError(f"commodity code {draft['commodityCode']} must resolve exactly once with isArchived=false; restart API if archive state is missing")
        mapped[draft["mapping"]] = found[0]
    markets = api.listing("/api/markets")
    market_matches = exact_code(markets, market_code)
    if len(market_matches) != 1 or market_matches[0].get("name", "").strip().casefold() not in spec["marketNames"]:
        raise ValueError(f"market code {market_code} must resolve to exactly one existing {market} market")
    units = api.listing("/api/measurement-units")
    matches = exact_code(units, "MG/KG")
    if len(matches) != 1 or matches[0].get("quantityKind", "").upper() != "RESIDUE_CONCENTRATION":
        raise ValueError("measurement unit MG/KG (RESIDUE_CONCENTRATION) must already exist before import")
    current = api.listing("/api/mrl-versions")
    refs = source_ref(market, resolved_date)
    by_pair = []
    for draft in drafts:
        commodity = mapped[draft["mapping"]]
        candidates = [v for v in current if v.get("marketId") == market_matches[0]["id"] and v.get("commodityId") == commodity["id"] and v.get("sourceRef") == refs]
        exact = []
        for candidate in candidates:
            effective_from = datetime.fromisoformat(str(candidate.get("effectiveFrom", "")).replace("Z", "+00:00"))
            expected_from = datetime.fromisoformat(resolved_date + "T00:00:00+00:00")
            if effective_from != expected_from or candidate.get("effectiveTo") is not None:
                raise ValueError(f"existing source version {candidate['id']} has different effective dates; refusing duplicate")
            limits = api.listing(f"/api/mrl-versions/{candidate['id']}/limits")
            sub_by_id = {x["id"]: x["code"] for x in substances}
            observed = {(sub_by_id.get(x["substanceId"]), Decimal(str(x["limitValue"])), x["unit"].lower()) for x in limits}
            expected = {(r["substance_id"], Decimal(r["mrl_value_mg_kg"]), "mg/kg") for r in draft["rows"]}
            if candidate.get("status") == "DRAFT" and observed == expected:
                exact.append(candidate)
            elif candidate.get("status") == "DRAFT":
                raise ValueError(f"existing source draft {candidate['id']} differs; refusing overwrite")
            else:
                raise ValueError(f"existing source version {candidate['id']} has status {candidate.get('status')}; refusing duplicate")
        if len(exact) > 1:
            raise ValueError("ambiguous exact source drafts exist: " + ", ".join(x["id"] for x in exact))
        draft["existing"] = exact[0] if exact else None
        draft["market"] = market_matches[0]
        draft["commodity"] = commodity
        draft["effectiveDate"] = resolved_date
        by_pair.append(draft)
    return existing_by_code, by_pair


def run(args):
    market = normalize_market(getattr(args, "source_market", "VIETNAM"))
    spec = MARKET_SPECS[market]
    effective_date = validate_date(getattr(args, "effective_from", None)) or spec["defaultEffectiveDate"]
    substance_rows, mrl_rows = load_csv(SUBSTANCES_FILE), load_csv(MRL_FILE)
    names, active, excluded, drafts = prepare(
        substance_rows,
        mrl_rows,
        getattr(args, "rice_commodity_code", None),
        getattr(args, "husked_rice_commodity_code", None),
        source_market=market,
        polished_code=getattr(args, "polished_rice_commodity_code", None),
        bran_code=getattr(args, "bran_commodity_code", None),
    )
    refs = source_ref(market, effective_date)
    report = {"sourceMarket": market, "substances": len(names), "activeNumericLimits": len(active), "noMrlExcluded": [{"code": x["substance_id"], "name": x["substance_name"], "status": x["status"], "commodity": x["commodity_name_en"], "notes": x["notes"]} for x in excluded], "draftGroups": [{"mapping": d["mapping"], "commodityCode": d["commodityCode"], "limits": [{"code": r["substance_id"], "value": r["mrl_value_mg_kg"], "unit": "mg/kg"} for r in d["rows"]]} for d in drafts], "sourceRef": json.loads(refs)}
    if not args.apply:
        print(json.dumps(report, ensure_ascii=False, indent=2))
        return
    if not args.base_url or not os.environ.get("GRAINLEDGER_IMPORT_TOKEN"):
        raise ValueError("--apply requires --base-url and GRAINLEDGER_IMPORT_TOKEN environment variable")
    if market == "VIETNAM" and (not getattr(args, "rice_commodity_code", None) or not getattr(args, "husked_rice_commodity_code", None)):
        raise ValueError("--apply requires --rice-commodity-code and --husked-rice-commodity-code")
    if not drafts or any(not d["commodityCode"] for d in drafts):
        required_flags = ", ".join(f"--{label}-commodity-code" for label, _ in spec["commodities"])
        raise ValueError(f"--apply for {market} requires at least one commodity mapping ({required_flags})")
    if not effective_date:
        raise ValueError(f"--apply for {market} requires --effective-from YYYY-MM-DD")
    market_code = getattr(args, "market_code", None) or spec["defaultMarketCode"]
    api = Api(args.base_url, os.environ["GRAINLEDGER_IMPORT_TOKEN"])
    existing, drafts = preflight(api, names, drafts, market_code, source_market=market, effective_date=effective_date)
    report["existingSubstances"] = sum(x is not None for x in existing.values())
    report["plannedSubstanceCreates"] = sum(x is None for x in existing.values())
    created = []
    try:
        for code, name in names.items():
            if existing[code] is None:
                result = api.request("POST", "/api/active-substances", {"code": code, "name": name})
                created.append(("substance", result.get("id")))
        for draft in drafts:
            if draft["existing"]:
                created.append(("reused draft", draft["existing"]["id"]))
                continue
            limits = [{"substanceCode": r["substance_id"], "limitValue": r["mrl_value_mg_kg"], "unit": "mg/kg"} for r in draft["rows"]]
            result = api.request("POST", "/api/mrl-versions/import", {"marketId": draft["market"]["id"], "commodityId": draft["commodity"]["id"], "effectiveFrom": effective_date + "T00:00:00Z", "effectiveTo": None, "sourceRef": refs, "limits": limits})
            created.append(("draft", result.get("id")))
    except (RuntimeError, urllib.error.URLError, OSError, http.client.HTTPException, ValueError) as exc:
        ids = ", ".join(f"{kind}={resource_id}" for kind, resource_id in created)
        raise RuntimeError(f"partial import; do not retry blindly. Already created/reused: {ids or 'none'}. Resolve state with GET before rerun. {exc}") from None
    report["resources"] = [{"kind": kind, "id": resource_id} for kind, resource_id in created]
    print(json.dumps(report, ensure_ascii=False, indent=2))

def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true", help="write substances and MRL drafts; default is offline preview")
    parser.add_argument("--base-url", help="API origin/base, e.g. https://host")
    parser.add_argument("--source-market", default="VIETNAM", help="Snapshot market: VIETNAM, EU, USA, JAPAN, KOREA, or CODEX")
    parser.add_argument("--market-code", help="Catalog market code (defaults to VN, EU, US, JP, KR, or CODEX)")
    parser.add_argument("--effective-from", help="Effective date YYYY-MM-DD (defaults to 2017-07-01 for VIETNAM)")
    parser.add_argument("--rice-commodity-code")
    parser.add_argument("--husked-rice-commodity-code")
    parser.add_argument("--polished-rice-commodity-code")
    parser.add_argument("--bran-commodity-code")
    args = parser.parse_args(argv)
    try:
        run(args)
    except (OSError, ValueError, RuntimeError, csv.Error) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

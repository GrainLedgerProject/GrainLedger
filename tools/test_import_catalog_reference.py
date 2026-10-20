import unittest
from decimal import Decimal
from unittest.mock import patch

import import_catalog_reference as importer


class ImportReferenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.substances = importer.load_csv(importer.SUBSTANCES_FILE)
        cls.mrl = importer.load_csv(importer.MRL_FILE)

    def test_source_groups_exact_five_limits_and_nine_exclusions(self):
        names, active, excluded, groups = importer.prepare(self.substances, self.mrl, "RICE-GRAIN", "RICE-HUSKED")
        self.assertEqual(len(names), 14)
        self.assertEqual(len(active), 5)
        self.assertEqual(len(excluded), 9)
        self.assertEqual({r["substance_id"]: r["mrl_value_mg_kg"] for r in active}, {"CBD005": "2", "ABA008": "0.1", "CPF006": "0.5", "LCH010": "1", "TBZ014": "1.5"})
        self.assertEqual([(g["mapping"], len(g["rows"])) for g in groups], [("rice", 3), ("husked-rice", 2)])
        self.assertTrue(all(r["effective_date"] == "2017-07-01" for r in active))
        self.assertEqual({r["version"] for r in active}, {"VN_TT50_2016"})
        preview = importer.prepare(self.substances, self.mrl, None, None)[3]
        self.assertEqual([(g["mapping"], len(g["rows"])) for g in preview], [("rice", 3), ("husked-rice", 2)])
    def test_rejects_bad_commodity_mappings(self):
        for rice, husked in (("PADDY", "HUSKED"), ("VN-PADDY-RICE", "HUSKED"), ("SAME", "same"), ("", "HUSKED")):
            with self.subTest(rice=rice, husked=husked), self.assertRaises(ValueError):
                importer.prepare(self.substances, self.mrl, rice, husked)

    def test_rejects_malformed_local_source_before_api_access(self):
        malformed = [dict(row) for row in self.mrl]
        next(row for row in malformed if row["market"] == "VIETNAM")["status"] = "BROKEN"
        with self.assertRaises(ValueError):
            importer.prepare(self.substances, malformed, "RICE", "HUSKED")
        args = type("Args", (), {"apply": True, "base_url": "https://example.invalid", "rice_commodity_code": "RICE", "husked_rice_commodity_code": "HUSKED", "market_code": "VN"})()
        with patch.object(importer, "preflight", side_effect=AssertionError("API must not be touched")):
            with patch.object(importer, "load_csv", side_effect=[self.substances, malformed]):
                with self.assertRaises(ValueError):
                    importer.run(args)

    def test_existing_substance_conflict_fails_before_any_post(self):
        names, _, _, groups = importer.prepare(self.substances, self.mrl, "RICE", "HUSKED")

        class FakeApi:
            def __init__(self):
                self.posts = 0

            def listing(self, path):
                if path == "/api/active-substances":
                    return [{"code": "CBD005", "name": "Wrong name"}]
                raise AssertionError("conflict should stop prerequisite reads")

            def request(self, method, path, body=None):
                if method == "POST":
                    self.posts += 1
                raise AssertionError("writes forbidden during preflight")

        api = FakeApi()
        with self.assertRaisesRegex(ValueError, "conflicts"):
            importer.preflight(api, names, groups, "VN")
        self.assertEqual(api.posts, 0)


    def test_commodity_archive_state_required_before_writes(self):
        names, _, _, groups = importer.prepare(self.substances, self.mrl, "RICE", "HUSKED")

        class FakeApi:
            def __init__(self, archive_value, include_state=True):
                self.archive_value = archive_value
                self.include_state = include_state
                self.posts = 0

            def listing(self, path):
                if path == "/api/active-substances":
                    return []
                if path == "/api/commodities":
                    rows = [{"id": "rice", "code": "RICE"}, {"id": "husked", "code": "HUSKED"}]
                    if self.include_state:
                        rows[0]["isArchived"] = self.archive_value
                        rows[1]["isArchived"] = False
                    return rows
                if path == "/api/markets":
                    return [{"id": "market", "code": "VN", "name": "Vietnam"}]
                if path == "/api/measurement-units":
                    return [{"code": "MG/KG", "quantityKind": "RESIDUE_CONCENTRATION"}]
                if path == "/api/mrl-versions":
                    return []
                raise AssertionError(path)

            def request(self, method, path, body=None):
                if method == "POST":
                    self.posts += 1
                raise AssertionError("preflight must not write")

        for api in (FakeApi(True), FakeApi(None, include_state=False)):
            with self.subTest(api=api), self.assertRaisesRegex(ValueError, "isArchived=false"):
                importer.preflight(api, names, groups, "VN")
            self.assertEqual(api.posts, 0)
        active_api = FakeApi(False)
        _, checked = importer.preflight(active_api, names, groups, "VN")
        self.assertEqual(len(checked), 2)
        self.assertEqual(active_api.posts, 0)


    def test_exact_drafts_reused_with_decimal_scale_difference(self):
        names, _, _, groups = importer.prepare(self.substances, self.mrl, "RICE", "HUSKED")
        substance_ids = {code: f"substance-{code}" for code in names}
        group_ids = {"rice": "draft-rice", "husked-rice": "draft-husked"}
        commodities = {"rice": "commodity-rice", "husked-rice": "commodity-husked"}

        class FakeApi:
            def listing(self, path):
                if path == "/api/active-substances":
                    return [{"id": substance_ids[code], "code": code, "name": name} for code, name in names.items()]
                if path == "/api/commodities":
                    return [{"id": commodities[label], "code": code, "isArchived": False} for label, code in (("rice", "RICE"), ("husked-rice", "HUSKED"))]
                if path == "/api/markets":
                    return [{"id": "market-vn", "code": "VN", "name": "Vietnam"}]
                if path == "/api/measurement-units":
                    return [{"code": "MG/KG", "quantityKind": "RESIDUE_CONCENTRATION"}]
                if path == "/api/mrl-versions":
                    return [{"id": group_ids[draft["mapping"]], "marketId": "market-vn", "commodityId": commodities[draft["mapping"]], "effectiveFrom": "2017-07-01T00:00:00Z", "effectiveTo": None, "sourceRef": importer.source_ref(), "status": "DRAFT"} for draft in groups]
                draft_id = path.split("/")[3]
                draft = next(d for d in groups if group_ids[d["mapping"]] == draft_id)
                return [{"substanceId": substance_ids[row["substance_id"]], "limitValue": float(Decimal(row["mrl_value_mg_kg"]).quantize(Decimal("0.000"))), "unit": "mg/kg"} for row in draft["rows"]]

            def request(self, method, path, body=None):
                raise AssertionError("rerun must reuse exact drafts, not write")

        _, checked = importer.preflight(FakeApi(), names, groups, "VN")
        self.assertEqual({d["existing"]["id"] for d in checked}, set(group_ids.values()))

    def test_rejects_header_injection_token_without_echoing_it(self):
        token = "private-token\nAuthorization: other"
        with self.assertRaises(ValueError) as caught:
            importer.Api("http://127.0.0.1:5080", token)
        self.assertNotIn(token, str(caught.exception))
    def test_other_destinations_group_active_limits_and_share_substances(self):
        eu_names, eu_active, eu_excluded, eu_groups = importer.prepare(self.substances, self.mrl, rice_code="RICE", source_market="EU")
        self.assertEqual(len(eu_names), 14)
        self.assertEqual((len(eu_active), len(eu_excluded)), (14, 0))
        self.assertEqual([(g["mapping"], len(g["rows"])) for g in eu_groups], [("rice", 14)])

        jp_names, jp_active, jp_excluded, jp_groups = importer.prepare(self.substances, self.mrl, husked_code="HUSKED", source_market="JAPAN")
        self.assertEqual(jp_names, eu_names)
        self.assertEqual((len(jp_active), len(jp_excluded)), (11, 3))
        self.assertEqual([(g["mapping"], len(g["rows"])) for g in jp_groups], [("husked-rice", 11)])

        kr_names, kr_active, kr_excluded, kr_groups = importer.prepare(self.substances, self.mrl, rice_code="RICE", source_market="KOREA")
        self.assertEqual((len(kr_active), len(kr_excluded)), (12, 2))
        self.assertEqual([(g["mapping"], len(g["rows"])) for g in kr_groups], [("rice", 12)])

        _, us_active, us_excluded, us_groups = importer.prepare(
            self.substances, self.mrl, rice_code="RICE", husked_code="HUSKED", polished_code="POLISHED", bran_code="BRAN", source_market="USA"
        )
        self.assertEqual((len(us_active), len(us_excluded)), (8, 8))
        self.assertEqual([(g["mapping"], len(g["rows"])) for g in us_groups], [("rice", 5), ("husked-rice", 1), ("polished-rice", 1), ("bran", 1)])

        _, cx_active, cx_excluded, cx_groups = importer.prepare(
            self.substances, self.mrl, husked_code="HUSKED", polished_code="POLISHED", bran_code="BRAN", source_market="CODEX"
        )
        self.assertEqual((len(cx_active), len(cx_excluded)), (10, 14))
        self.assertEqual([(g["mapping"], len(g["rows"])) for g in cx_groups], [("husked-rice", 5), ("polished-rice", 4), ("bran", 1)])

    def test_non_vietnam_preflight_requires_effective_date_and_reuses_draft(self):
        names, _, _, groups = importer.prepare(self.substances, self.mrl, rice_code="RICE", source_market="EU")
        with self.assertRaisesRegex(ValueError, "--effective-from"):
            importer.preflight(None, names, groups, "EU", source_market="EU")
        substance_ids = {code: f"substance-{code}" for code in names}

        class FakeEuApi:
            def listing(self, path):
                if path == "/api/active-substances":
                    return [{"id": substance_ids[code], "code": code, "name": name} for code, name in names.items()]
                if path == "/api/commodities":
                    return [{"id": "commodity-rice", "code": "RICE", "isArchived": False}]
                if path == "/api/markets":
                    return [{"id": "market-eu", "code": "EU", "name": "European Union"}]
                if path == "/api/measurement-units":
                    return [{"code": "MG/KG", "quantityKind": "RESIDUE_CONCENTRATION"}]
                if path == "/api/mrl-versions":
                    return [{"id": "draft-eu-rice", "marketId": "market-eu", "commodityId": "commodity-rice", "effectiveFrom": "2026-09-13T00:00:00Z", "effectiveTo": None, "sourceRef": importer.source_ref("EU", "2026-09-13"), "status": "DRAFT"}]
                return [{"substanceId": substance_ids[row["substance_id"]], "limitValue": float(Decimal(row["mrl_value_mg_kg"])), "unit": "mg/kg"} for row in groups[0]["rows"]]

        _, checked = importer.preflight(FakeEuApi(), names, groups, "EU", source_market="EU", effective_date="2026-09-13")
        self.assertEqual(checked[0]["existing"]["id"], "draft-eu-rice")

if __name__ == "__main__":
    unittest.main()

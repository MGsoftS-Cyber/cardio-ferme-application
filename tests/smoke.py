"""Run only against a disposable database containing synthetic patients."""

import concurrent.futures
import datetime as dt
import json
import os
import time
import unittest
import urllib.error
import urllib.request
import uuid

BASE = os.environ.get(
    "CARDIO_URL",
    "http://127.0.0.1:5080"
)


def call(path, data=None, token=None, method=None):
    headers = {"Content-Type": "application/json"}

    if token:
        headers["Authorization"] = "Bearer " + token

    request = urllib.request.Request(
        BASE + path,
        data=None if data is None else json.dumps(data).encode(),
        headers=headers,
        method=method or ("GET" if data is None else "POST")
    )

    try:
        response = urllib.request.urlopen(request, timeout=15)
    except urllib.error.HTTPError as error:
        response = error

    body = response.read()

    try:
        body = json.loads(body) if body else None
    except json.JSONDecodeError:
        body = body.decode()

    return response.status, body


class Workflows(unittest.TestCase):
    def test_core_workflows(self):
        suffix = uuid.uuid4().hex
        password = "Synthetic-Test-Password-2026!"

        status, body = call(
            "/auth/login?useCookies=false",
            {
                "email": os.environ["CARDIO_DOCTOR_EMAIL"],
                "password": os.environ["CARDIO_DOCTOR_PASSWORD"]
            }
        )

        self.assertEqual(status, 200, body)
        doctor = body["accessToken"]

        email = f"{suffix}@example.test"

        self.assertEqual(
            call(
                "/auth/register",
                {"email": email, "password": password}
            )[0],
            200
        )

        status, body = call(
            "/auth/login?useCookies=false",
            {"email": email, "password": password}
        )

        self.assertEqual(status, 200, body)
        patient_token = body["accessToken"]

        _, account = call("/api/me", token=patient_token)

        def create_patient(name):
            status, body = call(
                "/api/patients",
                {
                    "name": name,
                    "birthDate": "1990-01-01",
                    "phone": "SYNTHETIC"
                },
                doctor
            )

            self.assertEqual(status, 201, body)
            return body["id"]

        patient = create_patient("SYNTHETIC " + suffix)
        other = create_patient("OTHER SYNTHETIC " + suffix)

        self.assertEqual(
            call(
                f"/api/patients/{patient}/link",
                {
                    "userId": account["userId"],
                    "identityChecked": True
                },
                doctor
            )[0],
            204
        )

        self.assertEqual(
            call(
                f"/api/patients/{other}",
                token=patient_token
            )[0],
            404
        )

        self.assertEqual(
            call("/api/patients", token=patient_token)[0],
            403
        )

        start = (
            dt.datetime.now(dt.timezone.utc)
            + dt.timedelta(
                days=2,
                seconds=int(suffix[:6], 16)
            )
        )

        status, slot = call(
            "/api/slots",
            {
                "start": start.isoformat(),
                "end": (
                    start + dt.timedelta(minutes=30)
                ).isoformat(),
                "resourceKey": None
            },
            doctor
        )

        self.assertEqual(status, 200, slot)

        # Conflicting doctor slots must be rejected by PostgreSQL protections.
        overlap_status, overlap_body = call(
            "/api/slots",
            {"start": start.isoformat(),
             "end": (start + dt.timedelta(minutes=15)).isoformat(),
             "resourceKey": None},
            doctor
        )
        self.assertEqual(overlap_status, 409, overlap_body)

        def book(_):
            return call(
                "/api/appointments",
                {
                    "slotId": slot["id"],
                    "patientId": patient,
                    "requestKey": str(uuid.uuid4())
                },
                doctor
            )[0]

        with concurrent.futures.ThreadPoolExecutor(
            max_workers=2
        ) as pool:
            statuses = sorted(pool.map(book, range(2)))

        self.assertEqual(statuses, [200, 409])

        bmi_path = f"/api/patients/{patient}/calculations/bmi"

        status, result = call(
            bmi_path,
            {"weightKg": 80, "heightCm": 200},
            doctor
        )

        self.assertEqual((status, result["result"]), (200, 20))

        self.assertEqual(
            call(
                bmi_path,
                {"weightKg": 80, "heightCm": 0},
                doctor
            )[0],
            400
        )

        status, document = call(
            f"/api/patients/{patient}/documents",
            {
                "kind": "Certificate",
                "body": "SYNTHETIC TEST DOCUMENT",
                "lines": []
            },
            doctor
        )

        self.assertEqual(status, 200, document)
        document_id = document["id"]

        _, rows = call(
            f"/api/patients/{patient}/documents",
            token=patient_token
        )

        self.assertEqual(rows, [])

        self.assertEqual(
            call(
                f"/api/documents/{document_id}/sign",
                {"release": True},
                doctor
            )[0],
            200
        )

        self.assertEqual(
            call(
                f"/api/documents/{document_id}/print",
                token=patient_token
            )[0],
            200
        )

        self.assertEqual(
            call(
                f"/api/documents/{document_id}/sign",
                {"release": True},
                doctor
            )[0],
            400
        )

        # Prescription ownership joins must not interfere with row locking on sign.
        status, rx = call(
            f"/api/patients/{patient}/documents",
            {"kind": "Prescription", "body": "SYNTHETIC ONLY",
             "lines": [{"medicine": "SYNTHETIC TEST ITEM",
                        "instructions": "TEST DATA ONLY - not a treatment",
                        "quantity": 1}]},
            doctor
        )
        self.assertEqual(status, 200, rx)
        status, signed_rx = call(
            f"/api/documents/{rx['id']}/sign", {"release": True}, doctor
        )
        self.assertEqual(status, 200, signed_rx)
        self.assertEqual(len(signed_rx["lines"]), 1)

        status, invoice = call(
            f"/api/patients/{patient}/invoices",
            {
                "description": "Synthetic visit",
                "total": 100,
                "currency": "DZD"
            },
            doctor
        )

        self.assertEqual(status, 200, invoice)

        payment_path = f"/api/invoices/{invoice['id']}/payments"

        payment = {
            "amount": 60,
            "method": "Cash",
            "requestKey": str(uuid.uuid4())
        }

        first = call(payment_path, payment, doctor)
        second = call(payment_path, payment, doctor)

        self.assertEqual((first[0], second[0]), (200, 200))
        self.assertEqual(first[1]["id"], second[1]["id"])

        self.assertEqual(
            call(
                payment_path,
                {
                    "amount": 50,
                    "method": "Cash",
                    "requestKey": str(uuid.uuid4())
                },
                doctor
            )[0],
            400
        )


if __name__ == "__main__":
    for attempt in range(40):
        try:
            if call("/health")[0] == 200:
                break
        except (OSError, urllib.error.URLError):
            pass

        time.sleep(1)

    unittest.main()

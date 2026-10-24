"use strict";

let token = "";
let me = {};
let patients = [];

const $ = id => document.getElementById(id);

const has = role => me.roles?.includes(role);
const office = () => has("Doctor") || has("Receptionist");
const care = () => has("Doctor") || has("Nurse");

const active = () => {
  if (!$("patient").value) {
    throw Error("Select a patient first.");
  }

  return $("patient").value;
};

const notice = text => {
  $("notice").textContent = text;
};

const run = fn =>
  Promise.resolve()
    .then(fn)
    .catch(e => notice(e.message));

const fields = form =>
  Object.fromEntries(new FormData(form));

async function api(path, method = "GET", body, raw = false) {
  const headers = {
    Authorization: `Bearer ${token}`
  };

  if (body !== undefined && !(body instanceof FormData)) {
    headers["Content-Type"] = "application/json";
  }

  const response = await fetch("/api" + path, {
    method,
    headers,
    body: body === undefined
      ? undefined
      : body instanceof FormData
        ? body
        : JSON.stringify(body)
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));

    throw Error(
      problem.errors
        ? Object.values(problem.errors).flat().join("\n")
        : problem.title || `Request failed (${response.status}).`
    );
  }

  if (raw) {
    return response;
  }

  return response.status === 204
    ? null
    : response.json();
}

function bind(id, fn) {
  $(id).addEventListener("submit", event => {
    event.preventDefault();

    run(async () => {
      await fn(fields(event.target), event.target);
      notice("Saved.");
      await refresh();
    });
  });
}

function table(id, rows, columns, actions = () => []) {
  const target = $(id);
  target.replaceChildren();

  if (!rows.length) {
    target.textContent = "No records.";
    return;
  }

  const grid = document.createElement("table");
  const header = grid.insertRow();

  for (const [label] of columns) {
    const th = document.createElement("th");
    th.textContent = label;
    header.append(th);
  }

  header.append(document.createElement("th"));

  for (const row of rows) {
    const tr = grid.insertRow();

    for (const [, value] of columns) {
      tr.insertCell().textContent = String(value(row) ?? "");
    }

    const cell = tr.insertCell();

    for (const [label, fn] of actions(row)) {
      const button = document.createElement("button");
      button.type = "button";
      button.textContent = label;

      button.onclick = () => run(async () => {
        await fn();
        await refresh();
      });

      cell.append(button);
    }
  }

  target.append(grid);
}

const when = value =>
  new Date(value).toLocaleString();

const patientName = id =>
  patients.find(p => p.id === id)?.name || id;

async function refresh() {
  const selected = $("patient").value;

  patients = await api("/patients");

  $("patient").replaceChildren(
    ...patients.map(p =>
      new Option(`${p.name} — ${p.birthDate}`, p.id))
  );

  if (patients.some(p => p.id === selected)) {
    $("patient").value = selected;
  }

  const slots = await api("/slots");

  table(
    "slots",
    slots,
    [
      ["Start", x => when(x.start)],
      ["End", x => when(x.end)]
    ],
    x => [
      [
        "Book for active patient",
        () => api("/appointments", "POST", {
          slotId: x.id,
          patientId: active(),
          requestKey: crypto.randomUUID()
        })
      ],
      ...(has("Doctor")
        ? [[
            "Close",
            () => api(`/slots/${x.id}/close`, "POST")
          ]]
        : [])
    ]
  );

  const appointments = await api("/appointments");

  table(
    "appointments",
    appointments,
    [
      ["Patient", x => patientName(x.patientId)],
      ["Time", x => when(x.start)],
      ["Status", x => x.status]
    ],
    x => {
      const states = {
        Confirmed: ["CheckedIn", "Cancelled", "NoShow"],
        CheckedIn: ["InProgress", "Cancelled"],
        InProgress: ["Completed"]
      };

      return (states[x.status] || []).map(status => [
        status,
        () => api(`/appointments/${x.id}/status`, "POST", {
          status
        })
      ]);
    }
  );

  if (!$("patient").value) {
    return;
  }

  const base = "/patients/" + active();

  if (care()) {
    table(
      "entries",
      await api(base + "/entries"),
      [
        ["Type", x => x.kind],
        ["Title", x => x.title],
        ["Details", x => x.content],
        [
          "Value",
          x => x.value == null ? "" : `${x.value} ${x.unit}`
        ]
      ]
    );

    table(
      "documents",
      await api(base + "/documents"),
      [
        ["Type", x => x.kind],
        [
          "Status",
          x => x.cancelledAt
            ? "Cancelled"
            : x.signedAt ? "Finalized" : "Draft"
        ],
        ["Text", x => x.body]
      ],
      x => {
        if (x.cancelledAt) {
          return [];
        }

        if (!x.signedAt) {
          return has("Doctor")
            ? [[
                "Finalize and release",
                () => api(`/documents/${x.id}/sign`, "POST", {
                  release: true
                })
              ]]
            : [];
        }

        return [
          [
            "Print / save PDF",
            () => printDocument(x.id)
          ],
          ...(has("Doctor")
            ? [[
                "Cancel",
                () => api(`/documents/${x.id}/cancel`, "POST")
              ]]
            : [])
        ];
      }
    );

    table(
      "files",
      await api(base + "/files"),
      [
        ["Report", x => x.name],
        ["Released", x => x.released]
      ],
      x => [
        ["Download", () => downloadFile(x.id)],
        ...(!x.released && has("Doctor")
          ? [[
              "Release reviewed report",
              () => api(`/files/${x.id}/release`, "POST")
            ]]
          : [])
      ]
    );
  }

  if (has("Doctor")) {
    table(
      "notes",
      await api(base + "/consultations"),
      [
        ["Note", x => x.note],
        ["Status", x => x.signedAt ? "Finalized" : "Draft"]
      ],
      x => x.signedAt
        ? []
        : [
            [
              "Edit",
              async () => {
                const note = prompt("Edit consultation", x.note);

                if (note !== null) {
                  await api(`/consultations/${x.id}`, "PUT", {
                    note,
                    version: x.version
                  });
                }
              }
            ],
            [
              "Finalize",
              () => api(`/consultations/${x.id}/sign`, "POST")
            ]
          ]
    );
  }

  if (office()) {
    const invoices = await api(base + "/invoices");

    table(
      "invoices",
      invoices,
      [
        ["Service", x => x.description],
        ["Total", x => `${x.total} ${x.currency}`],
        ["Paid", x => x.paid],
        ["Balance", x => (x.total - x.paid).toFixed(2)]
      ]
    );

    $("invoice").replaceChildren(
      ...invoices
        .filter(x => x.total > x.paid)
        .map(x => new Option(
          `${x.description} — ${x.total - x.paid} ${x.currency}`,
          x.id))
    );
  }

  table(
    "messages",
    await api(base + "/messages"),
    [
      ["Date", x => when(x.createdAt)],
      ["Message", x => x.body]
    ]
  );
}

async function printDocument(id) {
  const popup = window.open("", "_blank");

  if (!popup) {
    throw Error("Allow the print window in your browser.");
  }

  popup.opener = null;

  try {
    const response = await api(
      `/documents/${id}/print`,
      "GET",
      undefined,
      true
    );

    popup.document.write(await response.text());
    popup.document.close();
    popup.focus();
    popup.print();
  } catch (e) {
    popup.close();
    throw e;
  }
}

async function downloadFile(id) {
  const response = await api(
    `/files/${id}`,
    "GET",
    undefined,
    true
  );

  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement("a");

  link.href = url;
  link.download = "report.pdf";
  link.click();

  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

$("login").onsubmit = event => {
  event.preventDefault();

  run(async () => {
    const response = await fetch("/auth/login?useCookies=false", {
      method: "POST",
      headers: {
        "Content-Type": "application/json"
      },
      body: JSON.stringify(fields(event.target))
    });

    if (!response.ok) {
      throw Error(
        "Sign-in failed. Check credentials or account lockout."
      );
    }

    token = (await response.json()).accessToken;
    me = await api("/me");

    if (!me.roles.some(x =>
      ["Doctor", "Nurse", "Receptionist"].includes(x))) {
      token = "";
      throw Error("Use the patient website or mobile app.");
    }

    event.target.reset();
    $("login").hidden = true;
    $("workspace").hidden = false;

    for (const node of document.querySelectorAll("[data-role]")) {
      const role = node.dataset.role;

      node.hidden = role === "Office"
        ? !office()
        : role === "Care"
          ? !care()
          : !has(role);
    }

    notice("Signed in. Session expires after 15 minutes.");
    await refresh();
  });
};

// The access token is kept only in memory.
$("logout").onclick = () => location.reload();

$("patient").onchange = () => run(refresh);
$("refresh").onclick = () => run(refresh);

bind("newPatient", data =>
  api("/patients", "POST", data));

bind("newSlot", data =>
  api("/slots", "POST", {
    start: new Date(data.start).toISOString(),
    end: new Date(data.end).toISOString(),
    resourceKey: data.resourceKey || null
  }));

bind("newEntry", data =>
  api(`/patients/${active()}/entries`, "POST", {
    ...data,
    value: data.value === "" ? null : Number(data.value),
    unit: data.unit || null,
    observedAt: new Date().toISOString()
  }));

bind("newNote", data =>
  api(`/patients/${active()}/consultations`, "POST", {
    ...data,
    version: crypto.randomUUID()
  }));

bind("newDocument", data =>
  api(`/patients/${active()}/documents`, "POST", {
    kind: data.kind,
    body: data.body,
    lines: data.kind === "Prescription"
      ? [{
          medicine: data.medicine,
          instructions: data.instructions,
          quantity: Number(data.quantity)
        }]
      : []
  }));

bind("newInvoice", data =>
  api(`/patients/${active()}/invoices`, "POST", {
    ...data,
    total: Number(data.total)
  }));

bind("newPayment", data =>
  api(`/invoices/${data.invoiceId}/payments`, "POST", {
    amount: Number(data.amount),
    method: data.method,
    requestKey: crypto.randomUUID()
  }));

bind("newMessage", data =>
  api(`/patients/${active()}/messages`, "POST", data));

bind("newFile", (_, form) =>
  api(
    `/patients/${active()}/files`,
    "POST",
    new FormData(form)
  ));

$("bmi").onsubmit = event => {
  event.preventDefault();

  run(async () => {
    const data = fields(event.target);

    const result = await api(
      `/patients/${active()}/calculations/bmi`,
      "POST",
      {
        weightKg: Number(data.weightKg),
        heightCm: Number(data.heightCm)
      }
    );

    notice(`BMI: ${result.result} ${result.unit}`);
  });
};

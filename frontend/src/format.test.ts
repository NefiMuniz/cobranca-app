import {
  addMonths,
  extendAccess,
  defaultDueDate,
  formatDate,
  formatMonth,
  monthsBetween,
  parseAmount,
  splitInstallments,
  statusLabel,
  todayIso,
  whatsappLink,
} from "./format";

describe("parseAmount", () => {
  it.each([
    ["600", 600],
    ["600,50", 600.5],
    ["1.234,56", 1234.56],
    ["R$ 1.500", 1500],
    ["99.9", 99.9],
    [" 10,999 ", 11],
  ])("parses %s as %d", (input, expected) => {
    expect(parseAmount(input)).toBe(expected);
  });

  it.each(["", "abc", "1,2,3", "12a"])("rejects %j", (input) => {
    expect(parseAmount(input)).toBeNull();
  });
});

describe("dates", () => {
  it("formats ISO dates without time zone shifts", () => {
    expect(formatDate("2026-01-05")).toBe("05/01/2026");
    expect(formatMonth("2026-11-01")).toBe("nov/2026");
  });

  it("defaults the due date to the 5th of the next month, rolling the year", () => {
    expect(defaultDueDate("2026-11")).toBe("2026-12-05");
    expect(defaultDueDate("2026-12")).toBe("2027-01-05");
  });

  it("uses the local calendar day for today", () => {
    expect(todayIso(new Date(2026, 10, 24, 23, 30))).toBe("2026-11-24");
  });
});

describe("statusLabel", () => {
  it("prioritizes overdue over the payment status", () => {
    expect(statusLabel({ status: "Open", isOverdue: true })).toBe("Vencida");
    expect(statusLabel({ status: "Partial", isOverdue: true })).toBe("Vencida (parcial)");
    expect(statusLabel({ status: "Partial", isOverdue: false })).toBe("Parcial");
    expect(statusLabel({ status: "Paid", isOverdue: false })).toBe("Paga");
  });
});

describe("whatsappLink", () => {
  it.each([
    ["(77) 99208-8601", "https://wa.me/5577992088601"],
    ["(77) 3422-1100", "https://wa.me/557734221100"],
    ["+55 77 99208-8601", "https://wa.me/5577992088601"],
    ["077 99208-8601", "https://wa.me/5577992088601"],
  ])("builds a click-to-chat link for %s", (phone, expected) => {
    expect(whatsappLink(phone)).toBe(expected);
  });

  it("returns null for incomplete numbers", () => {
    expect(whatsappLink("99208")).toBeNull();
  });
});

describe("plans", () => {
  it("adds months across years and counts months inclusively", () => {
    expect(addMonths("2027-11", 3)).toBe("2028-02");
    expect(monthsBetween("2027-01", "2027-06")).toBe(6);
    expect(monthsBetween("2027-11", "2028-02")).toBe(4);
  });

  it("splits installments with the remainder in the last one", () => {
    expect(splitInstallments(5000, 5)).toEqual([1000, 1000, 1000, 1000, 1000]);
    expect(splitInstallments(1000, 3)).toEqual([333.33, 333.33, 333.34]);
  });
});

describe("access period", () => {
  it("keeps remaining days when renewing an active account", () => {
    expect(extendAccess("2026-12-04", 30, "2026-11-24")).toBe("2027-01-03");
  });

  it("counts from today when expired or never granted", () => {
    expect(extendAccess("2026-11-01", 30, "2026-11-24")).toBe("2026-12-24");
    expect(extendAccess(null, 30, "2026-11-24")).toBe("2026-12-24");
  });

  it("adds a prefilled message to WhatsApp links", () => {
    expect(whatsappLink("(77) 99208-8601", "Olá!")).toBe("https://wa.me/5577992088601?text=Ol%C3%A1!");
  });
});

import type { Rule } from "./types";

const RULE_SERVICE_URL = process.env.RULE_SERVICE_URL ?? "http://localhost:5002";

const rules: Rule[] = [
  {
    $type: "WeightTier",
    type: "WeightTier",
    name: "Standard Weight Pricing",
    enabled: true,
    tiers: [
      { minKg: 0, maxKg: 5, pricePerKg: 10 },
      { minKg: 5, maxKg: 20, pricePerKg: 8 },
      { minKg: 20, maxKg: 100, pricePerKg: 6 },
    ],
  },
  {
    $type: "TimeWindowPromotion",
    type: "TimeWindowPromotion",
    name: "Lunch Hour Promo",
    enabled: true,
    startTime: "11:00",
    endTime: "13:00",
    discountPercent: 15,
  },
  {
    $type: "RemoteAreaSurcharge",
    type: "RemoteAreaSurcharge",
    name: "Remote Area Fee",
    enabled: true,
    remoteZipPrefixes: ["95", "96", "63"],
    surchargeFlat: 30,
  },
];

async function deleteAllRules(): Promise<void> {
  const res = await fetch(`${RULE_SERVICE_URL}/rules`);
  const existing: { id: string }[] = await res.json();

  for (const rule of existing) {
    await fetch(`${RULE_SERVICE_URL}/rules/${rule.id}`, { method: "DELETE" });
    console.log(`  Deleted rule ${rule.id}`);
  }
}

async function createRule(rule: Rule): Promise<void> {
  const res = await fetch(`${RULE_SERVICE_URL}/rules`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(rule),
  });

  const created = await res.json();
  console.log(`  Created [${rule.type}] "${rule.name}" → id: ${created.id}`);
}

async function main(): Promise<void> {
  console.log("Cleaning existing rules...");
  await deleteAllRules();

  console.log("\nSeeding rules...");
  for (const rule of rules) {
    await createRule(rule);
  }

  console.log("\nVerifying...");
  const res = await fetch(`${RULE_SERVICE_URL}/rules`);
  const all = await res.json();
  console.log(`  Total rules: ${all.length}`);
  console.log("\nDone!");
}

main().catch(console.error);

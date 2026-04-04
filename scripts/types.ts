// ══════════════════════════════════════════════
//  Rule Types — mirrors Shared/Class1.cs
// ══════════════════════════════════════════════

export interface WeightTier {
  minKg: number;
  maxKg: number;
  pricePerKg: number;
}

interface RuleBase {
  id?: string;
  name: string;
  enabled: boolean;
}

export interface WeightTierRule extends RuleBase {
  $type: "WeightTier";
  type: "WeightTier";
  tiers: WeightTier[];
}

export interface TimeWindowPromotionRule extends RuleBase {
  $type: "TimeWindowPromotion";
  type: "TimeWindowPromotion";
  startTime: string; // HH:mm format
  endTime: string; // HH:mm format
  discountPercent: number; // 10 = 10%
}

export interface RemoteAreaSurchargeRule extends RuleBase {
  $type: "RemoteAreaSurcharge";
  type: "RemoteAreaSurcharge";
  remoteZipPrefixes: string[];
  surchargeFlat: number;
}

export type Rule = WeightTierRule | TimeWindowPromotionRule | RemoteAreaSurchargeRule;

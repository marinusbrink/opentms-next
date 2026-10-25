import { useMemo, useState } from "react";
import { Play } from "lucide-react";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { MultiSelect } from "@/components/ui/multi-select";
import { ReferenceCombobox, type ReferenceOption } from "@/components/ui/reference-combobox";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useCreateInPlace, withId } from "@/apps/master-data/components/create-in-place";
import { parseNumber } from "@/apps/master-data/components/fields";
import { useSimulatePriceList, type PriceListSimulateDto, type PriceListSimulateResultDto } from "@/domains/financial/price-lists";
import { geographicalRegions, products, relationLookupPage, transportUnits, vehicleTypes, type RelationOption } from "@/domains/master-data/entities";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The Simulate tab of the price list editor (design 57 §UI): a line snapshot (the lane with its relations and regions,
 * the dimensions, the quantities, the as-of date) → the winning rule, the cell it selected, the price, and the ranked
 * candidates with why each lost. Nothing is stored: the panel answers "what would this list charge?". */

export interface PriceListSimulateTabProps {
  priceListId: string;
  currencyCode: string;
}

interface SimulateValues {
  fromRelation: RelationOption | null;
  fromRegionIds: string[];
  toRelation: RelationOption | null;
  toRegionIds: string[];
  transportUnitId: string | null;
  productId: string | null;
  vehicleTypeId: string | null;
  quantity: string;
  positions: string;
  colli: string;
  weightKg: string;
  volumeM3: string;
  loadingMeters: string;
  loadFactor: string;
  epq: string;
  distanceKm: string;
  tollDistanceKm: string;
  durationH: string;
  asOf: string | null;
}

const QUANTITY_FIELDS = [
  ["quantity", "Financial:Quantity"],
  ["positions", "Financial:QuantitySource.Positions"],
  ["colli", "Financial:QuantitySource.Colli"],
  ["weightKg", "Financial:QuantitySource.WeightKg"],
  ["volumeM3", "Financial:QuantitySource.VolumeM3"],
  ["loadingMeters", "Financial:QuantitySource.LoadingMeters"],
  ["loadFactor", "Financial:QuantitySource.LoadFactor"],
  ["epq", "Financial:QuantitySource.Epq"],
  ["distanceKm", "Financial:QuantitySource.DistanceKm"],
  ["tollDistanceKm", "Financial:QuantitySource.TollDistanceKm"],
  ["durationH", "Financial:QuantitySource.DurationH"],
] as const;
type QuantityField = (typeof QUANTITY_FIELDS)[number][0];

const initial = (): SimulateValues => ({
  fromRelation: null,
  fromRegionIds: [],
  toRelation: null,
  toRegionIds: [],
  transportUnitId: null,
  productId: null,
  vehicleTypeId: null,
  quantity: "1",
  positions: "",
  colli: "",
  weightKg: "",
  volumeM3: "",
  loadingMeters: "",
  loadFactor: "",
  epq: "",
  distanceKm: "",
  tollDistanceKm: "",
  durationH: "",
  asOf: null,
});

/** The snapshot the server takes; undefined when a required part is missing or a number is not a number. */
export function toSimulateDto(v: SimulateValues): PriceListSimulateDto | undefined {
  if (!v.fromRelation || !v.toRelation || !v.transportUnitId) return undefined;
  const numbers = Object.fromEntries(QUANTITY_FIELDS.map(([field]) => [field, parseNumber(v[field])])) as Record<QuantityField, number | null>;
  if (Object.values(numbers).some((n) => n != null && Number.isNaN(n))) return undefined;
  return {
    fromRelationId: v.fromRelation.id,
    fromRegionIds: v.fromRegionIds,
    toRelationId: v.toRelation.id,
    toRegionIds: v.toRegionIds,
    transportUnitId: v.transportUnitId,
    productId: v.productId,
    vehicleTypeId: v.vehicleTypeId,
    quantity: numbers.quantity ?? 1,
    positions: numbers.positions,
    colli: numbers.colli,
    weightKg: numbers.weightKg,
    volumeM3: numbers.volumeM3,
    loadingMeters: numbers.loadingMeters,
    loadFactor: numbers.loadFactor,
    epq: numbers.epq,
    distanceKm: numbers.distanceKm,
    tollDistanceKm: numbers.tollDistanceKm,
    durationH: numbers.durationH,
    asOf: v.asOf,
  };
}

export function PriceListSimulateTab({ priceListId, currencyCode }: PriceListSimulateTabProps) {
  const { t } = useL();
  const f = useFormat();
  const inPlace = useCreateInPlace();
  const simulate = useSimulatePriceList();
  const [values, setValues] = useState<SimulateValues>(initial);
  const [submitted, setSubmitted] = useState(false);
  const [result, setResult] = useState<PriceListSimulateResultDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [instance] = useState(() => `sim-${Math.random().toString(36).slice(2, 8)}`);
  const id = (field: string) => `${instance}-${field}`;
  const set = <K extends keyof SimulateValues>(field: K, value: SimulateValues[K]) => setValues((v) => ({ ...v, [field]: value }));

  const relationPage = useMemo(() => relationLookupPage(), []);
  const regionLookup = geographicalRegions.useLookup();
  const unitLookup = transportUnits.useLookup();
  const productLookup = products.useLookup();
  const vehicleTypeLookup = vehicleTypes.useLookup();
  const toOptions = (items: { id?: string; code?: string | null; name?: string | null }[] | undefined): ReferenceOption[] => (items ?? []).map((i) => ({ value: i.id ?? "", label: i.name ?? i.code ?? "" }));
  const regionOptions = useMemo(() => (regionLookup.data ?? []).map((r) => ({ id: r.id ?? "", label: r.name ?? "" })), [regionLookup.data]);
  const unitOptions = useMemo(() => toOptions(unitLookup.data), [unitLookup.data]);
  const productOptions = useMemo(() => toOptions(productLookup.data), [productLookup.data]);
  const vehicleTypeOptions = useMemo(() => toOptions(vehicleTypeLookup.data), [vehicleTypeLookup.data]);

  const dto = toSimulateDto(values);
  const required = (missing: boolean) => (submitted && missing ? t("MasterData:Required") : undefined);

  const run = async () => {
    setSubmitted(true);
    setError(null);
    if (!dto) return;
    try {
      setResult(await simulate.mutateAsync({ id: priceListId, dto }));
    } catch (err) {
      setResult(null);
      setError(getApiErrorMessage(err, t));
    }
  };

  const relationPicker = (field: "fromRelation" | "toRelation") => (
    <FormField label={t("Financial:Relation")} htmlFor={id(field)} required error={required(!values[field])} floating="always">
      <EntityLookup<RelationOption>
        id={id(field)}
        value={values[field]}
        onChange={(next) => set(field, next)}
        fetchPage={relationPage}
        getLabel={(r) => r.name}
        renderOption={(r) => (
          <>
            <div className="font-medium">{r.name}</div>
            <div className="text-label text-muted-foreground">{[r.code, [r.city, r.countryCode].filter(Boolean).join(" ")].filter(Boolean).join(", ")}</div>
          </>
        )}
        required
        aria-invalid={required(!values[field]) != null}
        {...inPlace.offer("Relation", false, (r) => set(field, { id: r.id, code: r.code ?? "", name: r.name, city: null, countryCode: null, roles: 0 }))}
      />
    </FormField>
  );
  const regionsPicker = (field: "fromRegionIds" | "toRegionIds") => (
    <FormField label={t("Financial:Regions")} htmlFor={id(field)} floating="always" hint={t("Financial:SimulateRegionsHint")}>
      <MultiSelect
        id={id(field)}
        value={values[field]}
        onChange={(next) => set(field, next)}
        options={regionOptions}
        {...inPlace.offer("GeographicalRegion", false, (r) => setValues((v) => ({ ...v, [field]: withId(v[field], r.id) })))}
      />
    </FormField>
  );
  const dimension = (field: "transportUnitId" | "productId" | "vehicleTypeId", labelKey: string, options: ReferenceOption[], entity: "TransportUnit" | "Product" | "VehicleType", isRequired: boolean) => (
    <FormField label={t(labelKey)} htmlFor={id(field)} required={isRequired} error={isRequired ? required(!values[field]) : undefined} floating="always">
      {/* DEVIATION(UI-LOOK-001): transport units, products and vehicle types are ReferenceComboboxes over their lookups (run-1 lists without a lookup-page endpoint), approved in docs/designs/48-master-data-vehicles.md (UI design). */}
      <ReferenceCombobox
        id={id(field)}
        value={values[field]}
        onChange={(next) => set(field, next)}
        options={options}
        required={isRequired}
        placeholder={isRequired ? undefined : t("Financial:Any")}
        aria-invalid={isRequired && required(!values[field]) != null}
        {...inPlace.offer(entity, false, (r) => set(field, r.id))}
      />
    </FormField>
  );

  const winner = result?.candidates?.find((c) => c.ruleId === result.winningRuleId);

  return (
    <div className="flex h-full min-h-0 flex-col gap-3">
      <p className="text-label text-muted-foreground">{t("Financial:SimulateHint")}</p>
      <div className="min-h-0 flex-1 overflow-y-auto pr-1">
        <div className="grid grid-cols-2 gap-x-4">
          <FormSection title={t("Financial:From")}>
            <div className="grid grid-cols-1 gap-y-3">
              {relationPicker("fromRelation")}
              {regionsPicker("fromRegionIds")}
            </div>
          </FormSection>
          <FormSection title={t("Financial:To")}>
            <div className="grid grid-cols-1 gap-y-3">
              {relationPicker("toRelation")}
              {regionsPicker("toRegionIds")}
            </div>
          </FormSection>
        </div>
        <FormSection title={t("Financial:Dimensions")}>
          <div className="grid grid-cols-3 gap-x-4 gap-y-3">
            {dimension("transportUnitId", "Financial:TransportUnit", unitOptions, "TransportUnit", true)}
            {dimension("productId", "Financial:Product", productOptions, "Product", false)}
            {dimension("vehicleTypeId", "Financial:VehicleType", vehicleTypeOptions, "VehicleType", false)}
          </div>
        </FormSection>
        <FormSection title={t("Financial:Quantities")}>
          <div className="grid grid-cols-4 gap-x-4 gap-y-3">
            {QUANTITY_FIELDS.map(([field, labelKey]) => {
              const n = parseNumber(values[field]);
              return (
                <FormField key={field} label={t(labelKey)} htmlFor={id(field)} required={field === "quantity"} error={submitted && n != null && Number.isNaN(n) ? t("MasterData:InvalidNumber") : undefined}>
                  <Input id={id(field)} inputMode="decimal" value={values[field]} onChange={(e) => set(field, e.target.value)} />
                </FormField>
              );
            })}
            <FormField label={t("Financial:AsOf")} htmlFor={id("asOf")} floating="always" hint={t("Financial:AsOfHint")}>
              <DatePicker id={id("asOf")} value={values.asOf} onChange={(next) => set("asOf", next)} />
            </FormField>
          </div>
        </FormSection>
        <div className="flex items-center gap-3 py-2">
          <Button type="button" onClick={() => void run()} disabled={simulate.isPending}>
            <Play /> {simulate.isPending ? t("Financial:Simulating") : t("Financial:RunSimulation")}
          </Button>
        </div>
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        {result && (
          <FormSection title={t("Financial:SimulationResult")}>
            <div className="flex flex-col gap-3" data-slot="simulation-result">
              {result.warning && (
                <Alert variant="warning">
                  {/* The warning is a localization key (FinancialErrorCodes.*Warning). */}
                  <AlertDescription>{t(result.warning)}</AlertDescription>
                </Alert>
              )}
              {!result.isValidOnAsOf && (
                <Alert variant="warning">
                  <AlertDescription>{t("Financial:NotValidOnAsOfHint")}</AlertDescription>
                </Alert>
              )}
              <dl className="grid grid-cols-4 gap-x-4 gap-y-1 text-sm">
                <dt className="text-label text-muted-foreground">{t("Financial:WinningRule")}</dt>
                <dd className="tabular-nums">{winner ? `${t("Financial:Rule")} ${winner.position}` : t("Financial:NoWinningRule")}</dd>
                <dt className="text-label text-muted-foreground">{t("Financial:Column")}</dt>
                <dd className="tabular-nums">{result.columnPosition ?? ""}</dd>
                <dt className="text-label text-muted-foreground">{t("Financial:UnitPrice")}</dt>
                <dd className="tabular-nums">{result.unitPrice == null ? "" : `${f.unitPrice(result.unitPrice)} ${currencyCode}`}</dd>
                <dt className="text-label text-muted-foreground">{t("Financial:Quantity")}</dt>
                <dd className="tabular-nums">{result.quantity == null ? "" : f.number(result.quantity, { maxDecimals: 4 })}</dd>
                <dt className="text-label text-muted-foreground">{t("Financial:Amount")}</dt>
                <dd className="font-semibold tabular-nums">{result.amount == null ? "" : `${f.amount(result.amount)} ${currencyCode}`}</dd>
              </dl>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t("Financial:Rule")}</TableHead>
                    <TableHead>{t("Financial:Tier")}</TableHead>
                    <TableHead>{t("Financial:LaneScore")}</TableHead>
                    <TableHead>{t("Financial:Reason")}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {(result.candidates ?? []).map((c) => (
                    <TableRow key={c.ruleId} data-winner={c.ruleId === result.winningRuleId ? "true" : undefined}>
                      <TableCell className="tabular-nums">{c.position}</TableCell>
                      <TableCell>
                        <Badge variant={(c.tier ?? 0) > 0 ? "default" : "secondary"}>{c.tier}</Badge>
                      </TableCell>
                      <TableCell className="tabular-nums">{c.laneScore}</TableCell>
                      <TableCell>{t(`Financial:Reason.${c.reason ?? "Candidate"}`)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          </FormSection>
        )}
      </div>
      {inPlace.dialog}
    </div>
  );
}

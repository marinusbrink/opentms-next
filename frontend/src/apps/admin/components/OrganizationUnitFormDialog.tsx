import { useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Save } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import type { AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { EntityDialog, type EntityDialogTab } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { recordIdentificationTab } from "@/components/ui/record-identification-tab";
import { Skeleton } from "@/components/ui/skeleton";
import { buildTree, collectSubtreeIds, TreeSelect, type TreeSelectNode } from "@/components/ui/tree-select";
import { OrganizationUnitMembersPanel } from "@/apps/admin/components/OrganizationUnitMembersPanel";
import {
  membersQueryKey,
  useAddOrganizationUnitMembers,
  useAllOrganizationUnits,
  useCreateOrganizationUnit,
  useOrganizationUnitMembers,
  useRemoveOrganizationUnitMembers,
  useUpdateOrganizationUnit,
  type OrganizationUnitRow,
} from "@/domains/platform/administration-organization-units";
import type { UserRow } from "@/domains/platform/administration-users";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface OrganizationUnitFormDialogProps {
  mode: "create" | "edit";
  unit?: OrganizationUnitRow;
  /** Create mode: the unit the new one goes under, prefilled in the parent picker. */
  parent?: OrganizationUnitRow;
  /** UI-PERM-004: view-only when the user may not update. */
  readOnly?: boolean;
  open: boolean;
  onClose: () => void;
  onSuccess: () => void;
}

interface FormValues {
  displayName: string;
  parentId: string | null;
}

const normalise = (v: FormValues) => ({ displayName: v.displayName.trim() || null, parentId: v.parentId });
const sortedIds = (rows: UserRow[]) => rows.map((r) => r.id ?? "").sort();

/* The one organization-unit editor for create, edit and view (UI-DLG-009). General holds the
 * name and the parent (a TreeSelect over the tenant's tree; changing it moves the unit with
 * its subtree). Members, in edit mode, is the unit's user list staged on the editor's state:
 * Add and Remove change the list, Save applies the difference, Close without saving drops it
 * (PO decision 2026-10-10). */
export function OrganizationUnitFormDialog({
  mode,
  unit,
  parent,
  readOnly = false,
  open,
  onClose,
  onSuccess,
}: OrganizationUnitFormDialogProps) {
  const { t } = useL();
  const queryClient = useQueryClient();
  const canManageMembers = useIsGranted("Platform.Administration.OrganizationUnits.ManageMembers") && !readOnly;
  const createUnit = useCreateOrganizationUnit();
  const updateUnit = useUpdateOrganizationUnit();
  const addMembers = useAddOrganizationUnitMembers();
  const removeMembers = useRemoveOrganizationUnitMembers();
  const allUnits = useAllOrganizationUnits();
  const membersQuery = useOrganizationUnitMembers(mode === "edit" ? (unit?.id ?? "") : "");
  const formRef = useRef<HTMLFormElement>(null);

  const initial = useMemo<FormValues>(
    () => ({
      displayName: mode === "edit" ? (unit?.displayName ?? "") : "",
      parentId: mode === "edit" ? (unit?.parentId ?? null) : (parent?.id ?? null),
    }),
    [mode, unit, parent],
  );

  const [values, setValues] = useState<FormValues>(initial);
  const [nameTouched, setNameTouched] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState("general");

  /* The staged member list starts from the server's list once it has loaded. */
  const [members, setMembers] = useState<UserRow[] | null>(null);
  const originalMembers = useRef<UserRow[]>([]);
  useEffect(() => {
    if (mode === "edit" && membersQuery.data && members === null) {
      originalMembers.current = membersQuery.data;
      setMembers(membersQuery.data);
    }
  }, [mode, membersQuery.data, members]);

  const tree = useMemo<TreeSelectNode[]>(
    () =>
      buildTree(
        allUnits.data ?? [],
        (u) => u.id ?? "",
        (u) => u.parentId,
        (u) => u.displayName ?? "",
      ),
    [allUnits.data],
  );
  /* A unit cannot become its own descendant. */
  const disabledParentIds = useMemo(
    () => (mode === "edit" && unit?.id ? collectSubtreeIds(tree, unit.id) : new Set<string>()),
    [mode, tree, unit?.id],
  );

  const isPending = createUnit.isPending || updateUnit.isPending || addMembers.isPending || removeMembers.isPending;
  const membersDirty =
    members !== null && JSON.stringify(sortedIds(members)) !== JSON.stringify(sortedIds(originalMembers.current));
  const isDirty = JSON.stringify(normalise(values)) !== JSON.stringify(normalise(initial)) || membersDirty;
  const nameError = values.displayName.trim() ? undefined : t("FieldRequired");
  const shownNameError = nameTouched || submitted ? nameError : undefined;

  const handleSubmit = async (e?: FormEvent) => {
    e?.preventDefault();
    if (readOnly || isPending) return;
    setSubmitted(true);
    setServerError(null);
    if (nameError) {
      setActiveTab("general");
      requestAnimationFrame(() => formRef.current?.querySelector<HTMLElement>("#ouf-name")?.focus());
      return;
    }

    try {
      if (mode === "create") {
        await createUnit.mutateAsync({ displayName: values.displayName.trim(), parentId: values.parentId });
      } else {
        const id = unit!.id!;
        if (JSON.stringify(normalise(values)) !== JSON.stringify(normalise(initial))) {
          await updateUnit.mutateAsync({ id, dto: { displayName: values.displayName.trim(), parentId: values.parentId } });
        }
        if (membersDirty && members) {
          const before = new Set(sortedIds(originalMembers.current));
          const after = new Set(sortedIds(members));
          const added = [...after].filter((memberId) => !before.has(memberId));
          const removed = [...before].filter((memberId) => !after.has(memberId));
          if (added.length > 0) {
            await addMembers.mutateAsync({ id, selection: { mode: "Explicit", explicitIds: added, filterRequest: null, excludedIds: [] } });
          }
          if (removed.length > 0) {
            await removeMembers.mutateAsync({ id, selection: { mode: "Explicit", explicitIds: removed, filterRequest: null, excludedIds: [] } });
          }
          void queryClient.invalidateQueries({ queryKey: membersQueryKey(id) });
        }
      }
      void queryClient.invalidateQueries({ queryKey: ["administration", "organization-units", "all"] });
      onSuccess();
      onClose();
      toast.success(t("Administration:OrganizationUnitSaved"));
    } catch (err) {
      setServerError(getApiErrorMessage(err, t));
    }
  };

  const title =
    mode === "create"
      ? parent
        ? t("Administration:NewSubUnit")
        : t("Administration:NewOrganizationUnit")
      : `${t("Administration:OrganizationUnit")} ${unit?.displayName ?? ""}`;

  const commands: AppCommandBarCommand[] = readOnly
    ? []
    : [
        {
          id: "save",
          labelKey: isPending ? "Saving" : "Save",
          icon: Save,
          isPrimary: true,
          onClick: () => formRef.current?.requestSubmit(),
        },
      ];

  const generalTab: EntityDialogTab = {
    value: "general",
    label: t("General"),
    hasError: shownNameError != null,
    content: (
      <form
        ref={formRef}
        onSubmit={(e) => void handleSubmit(e)}
        className="grid grid-cols-1 gap-4"
        noValidate
      >
        {serverError && (
          <Alert variant="destructive">
            <AlertDescription>{serverError}</AlertDescription>
          </Alert>
        )}

        {/* One topic: the section carries the entity's name (UI-FORM-005). */}
        <FormSection title={t("Administration:OrganizationUnit")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <FormField label={t("Administration:DisplayName")} htmlFor="ouf-name" required error={shownNameError}>
              <Input
                id="ouf-name"
                value={values.displayName}
                onChange={(e) => setValues((v) => ({ ...v, displayName: e.target.value }))}
                onBlur={() => setNameTouched(true)}
                maxLength={128}
                disabled={readOnly}
                autoComplete="off"
              />
            </FormField>

            <FormField label={t("Administration:ParentUnit")} htmlFor="ouf-parent" floating="always">
              <TreeSelect
                id="ouf-parent"
                nodes={tree}
                value={values.parentId}
                onChange={(parentId) => setValues((v) => ({ ...v, parentId }))}
                noneLabel={t("Administration:NoParentUnit")}
                disabledIds={disabledParentIds}
                disabled={readOnly || allUnits.isLoading}
              />
            </FormField>
          </div>
        </FormSection>

        {/* Enter submits a small editor (UI-KEY-001). */}
        <button type="submit" className="hidden" aria-hidden="true" tabIndex={-1} />
      </form>
    ),
  };

  const membersTab: EntityDialogTab | null =
    mode === "edit" && unit?.id
      ? {
          value: "members",
          label: t("Administration:Members"),
          content:
            members === null ? (
              <Skeleton className="h-96 w-full" />
            ) : (
              <OrganizationUnitMembersPanel
                unitId={unit.id}
                unitName={unit.displayName ?? ""}
                members={members}
                onChange={setMembers}
                canManage={canManageMembers}
              />
            ),
        }
      : null;

  const tabs: EntityDialogTab[] = [
    generalTab,
    ...(membersTab ? [membersTab] : []),
    recordIdentificationTab(t, {
      id: mode === "edit" ? unit?.id : undefined,
      creationTime: unit?.creationTime,
      lastModificationTime: unit?.lastModificationTime,
    }),
  ];

  return (
    <EntityDialog
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="editor"
      size="md"
      title={title}
      commands={commands}
      tabs={tabs}
      activeTab={activeTab}
      onActiveTabChange={setActiveTab}
      isDirty={isDirty}
      isBusy={isPending}
    />
  );
}

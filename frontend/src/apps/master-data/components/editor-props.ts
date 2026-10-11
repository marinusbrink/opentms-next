/* The props every entity editor takes from MasterDataListView.renderEditor (UI-DLG-009: one editor
 * per entity for create and edit; readOnly without the Update permission, UI-PERM-004). */
export interface EntityEditorProps {
  mode: "create" | "edit";
  id?: string;
  readOnly: boolean;
  open: boolean;
  onClose: () => void;
  onSaved: () => void;
}

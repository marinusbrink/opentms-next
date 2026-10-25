/* The props every entity editor takes from MasterDataListView.renderEditor (UI-DLG-009: one editor
 * per entity for create and edit; readOnly without the Update permission, UI-PERM-004). */
export interface EntityEditorProps {
  mode: "create" | "edit";
  id?: string;
  readOnly: boolean;
  open: boolean;
  onClose: () => void;
  /** Called after a successful save or delete; a save passes the record the API returned. */
  onSaved: (saved?: unknown) => void;
  /** Create mode only: the name a lookup's "Add new …" row was typed with (UI-LOOK-004). */
  initialName?: string;
  /** Replaces this editor by the editor of another record of the same list (a copy the editor created). */
  onOpenRecord?: (id: string) => void;
}

import { useCallback, useEffect, useImperativeHandle, useMemo, useRef, useState, type ComponentType, type FocusEvent, type Ref } from "react";
import { EditorContent, Extension, getHTMLFromFragment, useEditor, useEditorState, type Editor } from "@tiptap/react";
import { Plugin, PluginKey } from "@tiptap/pm/state";
import type { Node as PmNode } from "@tiptap/pm/model";
import StarterKit from "@tiptap/starter-kit";
import TextAlign from "@tiptap/extension-text-align";
import {
  Bold,
  CodeXml,
  Heading1,
  Heading2,
  Heading3,
  Image as ImageIcon,
  Italic,
  Link as LinkIcon,
  List,
  ListOrdered,
  Pilcrow,
  Redo2,
  Strikethrough,
  TextAlignCenter,
  TextAlignEnd,
  TextAlignStart,
  TriangleAlert,
  Underline,
  Undo2,
} from "lucide-react";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { isLosslessRoundTrip } from "@/components/ui/rich-text-fidelity";
import { isAllowedImageSrc, RichTextImage } from "@/components/ui/rich-text-image";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* RichTextEditor (component map GAP-08, decided 2026-10-18: Tiptap). HTML in, HTML out, like the other form controls:
 * `value` / `onChange(html)` / `onBlur`, and it sits in a FormField (UI-FORM-001), which injects aria-required,
 * aria-invalid, aria-describedby and a layout className. The label is wired with `aria-labelledby` (a contenteditable
 * is not labelable by <label for>).
 *
 * - Scriban placeholders `{{ … }}` typed in the WYSIWYG view stay plain text. Blocks that wrap HTML
 *   (`{{ for x in y }}<tr>…`) and markup Tiptap's schema does not know only survive in the source view: that view is a
 *   monospace textarea on the raw value, and switching back to WYSIWYG does not rewrite the value until the user
 *   edits there.
 * - Content set from outside (loading a record, a form reset) never emits onChange, so it never makes a form dirty;
 *   the own edits do not reset the editor (the cursor stays where it is).
 * - An empty document is emitted as "" (not "<p></p>"), so a required check on the value works.
 * - `maxLength` counts characters of the HTML: an edit that would make the HTML longer is refused, silently, as a
 *   textarea's maxLength does; messages are the form's job. Content set from outside is never refused.
 * - `preserveUnsupportedMarkup` (default on, PO 2026-10-18): a value the visual view cannot hold without loss (a table,
 *   a div, a class or style it does not know, a comment; rich-text-fidelity.ts) opens in the source view with a
 *   warning. Switching to the visual view stays possible; the warning stays while the value is lossy. The check runs on
 *   every value from outside and whenever the user switches back from the source view.
 * - Images (rich-text-image.ts): inline `<img>` with src, alt, title, width and height as attributes; the toolbar
 *   inserts one by URL. Only http(s), relative and Scriban (`{{ … }}`) sources are rendered; anything else is dropped.
 * - `transformOutput` rewrites what the visual view emits (e.g. HtmlBodyField un-escapes Scriban regions). It runs
 *   here, not in the caller's onChange, so the transformed value counts as an own edit and does not reset the
 *   editor. The source view emits the textarea's text as typed.
 * - `ref` (React 19: a prop) gives a RichTextEditorHandle: `insertText` puts text at the cursor as plain text, in the
 *   view that is open (the e-mail template's Tags panel inserts `{{ … }}` with it; a tag is never parsed as HTML), and
 *   `focus` focuses that view. An insert is an own edit: it emits onChange like typing, and respects `maxLength`. */

export interface RichTextEditorHandle {
  /** Inserts `text` at the cursor (replacing a selection) as plain text; line breaks become hard breaks in the visual view. */
  insertText: (text: string) => void;
  focus: () => void;
}

export interface RichTextEditorProps {
  /** The HTML. "" is an empty document. */
  value: string;
  onChange: (html: string) => void;
  onBlur?: () => void;
  /** The id of the editable element (the contenteditable, or the textarea in the source view). */
  id: string;
  readOnly?: boolean;
  disabled?: boolean;
  /** The maximum length of the HTML, in characters. */
  maxLength?: number;
  "aria-invalid"?: boolean;
  "aria-required"?: boolean;
  "aria-describedby"?: string;
  /** The label's id; FormField renders its label as `${htmlFor}-label`. */
  "aria-labelledby"?: string;
  /** Injected by FormField; there is no placeholder (UI-FORM-003). */
  placeholder?: string;
  /** Layout only: the height of the whole control, e.g. h-96. */
  className?: string;
  /** Open a value the visual view would change in the source view, with a warning (default true). */
  preserveUnsupportedMarkup?: boolean;
  /** Rewrites the visual view's HTML before it is emitted. */
  transformOutput?: (html: string) => string;
  ref?: Ref<RichTextEditorHandle>;
}

interface ToolState {
  bold: boolean;
  italic: boolean;
  underline: boolean;
  strike: boolean;
  paragraph: boolean;
  h1: boolean;
  h2: boolean;
  h3: boolean;
  bulletList: boolean;
  orderedList: boolean;
  link: boolean;
  alignLeft: boolean;
  alignCenter: boolean;
  alignRight: boolean;
  canUndo: boolean;
  canRedo: boolean;
}

const htmlOf = (editor: Editor, cache: WeakMap<PmNode, string>): string => {
  if (editor.isEmpty) return "";
  const doc = editor.state.doc;
  return cache.get(doc) ?? getHTMLFromFragment(doc.content, editor.schema);
};

/** Refuses an own edit that makes the HTML longer than the limit; caches the HTML it computed for onUpdate. */
const maxHtmlLength = (limit: () => number | undefined, cache: WeakMap<PmNode, string>) =>
  Extension.create({
    name: "maxHtmlLength",
    addProseMirrorPlugins() {
      const schema = this.editor.schema;
      return [
        new Plugin({
          key: new PluginKey("maxHtmlLength"),
          filterTransaction: (tr) => {
            const max = limit();
            if (max == null || !tr.docChanged || tr.getMeta("preventUpdate")) return true;
            const html = getHTMLFromFragment(tr.doc.content, schema);
            cache.set(tr.doc, html);
            return html.length <= max;
          },
        }),
      ];
    },
  });

function ToolButton({
  label,
  icon: Icon,
  onClick,
  pressed,
  disabled,
}: {
  label: string;
  icon: ComponentType<{ "aria-hidden"?: boolean }>;
  onClick: () => void;
  pressed?: boolean;
  disabled?: boolean;
}) {
  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            aria-label={label}
            aria-pressed={pressed}
            disabled={disabled}
            /* Keep the editor's selection: the command runs on it. */
            onMouseDown={(e) => e.preventDefault()}
            onClick={onClick}
            className="aria-pressed:bg-brand-subtle aria-pressed:text-brand"
          />
        }
      >
        <Icon aria-hidden />
      </TooltipTrigger>
      <TooltipContent>{label}</TooltipContent>
    </Tooltip>
  );
}

function LinkButton({ editor, active, disabled, idPrefix }: { editor: Editor; active: boolean; disabled: boolean; idPrefix: string }) {
  const { t } = useL();
  const [open, setOpen] = useState(false);
  const [url, setUrl] = useState("");
  const inputId = `${idPrefix}-link-url`;

  const apply = () => {
    const href = url.trim();
    if (!href) {
      editor.chain().focus().extendMarkRange("link").unsetLink().run();
    } else if (editor.state.selection.empty && !editor.isActive("link")) {
      editor.chain().focus().insertContent({ type: "text", text: href, marks: [{ type: "link", attrs: { href } }] }).run();
    } else {
      editor.chain().focus().extendMarkRange("link").setLink({ href }).run();
    }
    setOpen(false);
  };

  const remove = () => {
    editor.chain().focus().extendMarkRange("link").unsetLink().run();
    setOpen(false);
  };

  return (
    <Popover
      open={open}
      onOpenChange={(next) => {
        if (next) setUrl((editor.getAttributes("link").href as string | undefined) ?? "");
        setOpen(next);
      }}
    >
      <Tooltip>
        <TooltipTrigger
          render={
            <PopoverTrigger
              render={
                <Button
                  type="button"
                  variant="ghost"
                  size="icon-sm"
                  aria-label={t("RichText:Link")}
                  aria-pressed={active}
                  disabled={disabled}
                  onMouseDown={(e) => e.preventDefault()}
                  className="aria-pressed:bg-brand-subtle aria-pressed:text-brand"
                />
              }
            />
          }
        >
          <LinkIcon aria-hidden />
        </TooltipTrigger>
        <TooltipContent>{t("RichText:Link")}</TooltipContent>
      </Tooltip>
      <PopoverContent align="start" className="w-80 gap-3 p-3">
        <FormField label={t("RichText:LinkUrl")} htmlFor={inputId} floating="always">
          <Input
            id={inputId}
            value={url}
            autoFocus
            onChange={(e) => setUrl(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                e.preventDefault();
                apply();
              }
            }}
          />
        </FormField>
        <div className="flex justify-end gap-2">
          {active && (
            <Button type="button" variant="outline" onClick={remove}>
              {t("RichText:RemoveLink")}
            </Button>
          )}
          <Button type="button" onClick={apply}>
            {t("RichText:ApplyLink")}
          </Button>
        </div>
      </PopoverContent>
    </Popover>
  );
}

function ImageButton({ editor, disabled, idPrefix }: { editor: Editor; disabled: boolean; idPrefix: string }) {
  const { t } = useL();
  const [open, setOpen] = useState(false);
  const [url, setUrl] = useState("");
  const [alt, setAlt] = useState("");
  const urlId = `${idPrefix}-image-url`;
  const altId = `${idPrefix}-image-alt`;
  /* The same guard the node applies: a source it would not render is not inserted (the button stays disabled). */
  const allowed = isAllowedImageSrc(url.trim());

  const insert = () => {
    if (!allowed) return;
    const description = alt.trim();
    editor
      .chain()
      .focus()
      .setImage({ src: url.trim(), ...(description ? { alt: description } : {}) })
      .run();
    setOpen(false);
  };

  return (
    <Popover
      open={open}
      onOpenChange={(next) => {
        if (next) {
          setUrl("");
          setAlt("");
        }
        setOpen(next);
      }}
    >
      <Tooltip>
        <TooltipTrigger
          render={
            <PopoverTrigger
              render={
                <Button type="button" variant="ghost" size="icon-sm" aria-label={t("RichText:Image")} disabled={disabled} onMouseDown={(e) => e.preventDefault()} />
              }
            />
          }
        >
          <ImageIcon aria-hidden />
        </TooltipTrigger>
        <TooltipContent>{t("RichText:Image")}</TooltipContent>
      </Tooltip>
      <PopoverContent align="start" className="w-80 gap-3 p-3">
        <FormField label={t("RichText:ImageUrl")} htmlFor={urlId} floating="always">
          <Input
            id={urlId}
            value={url}
            autoFocus
            onChange={(e) => setUrl(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                e.preventDefault();
                insert();
              }
            }}
          />
        </FormField>
        <FormField label={t("RichText:ImageAlt")} htmlFor={altId} floating="always">
          <Input
            id={altId}
            value={alt}
            onChange={(e) => setAlt(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                e.preventDefault();
                insert();
              }
            }}
          />
        </FormField>
        <div className="flex justify-end gap-2">
          <Button type="button" disabled={!allowed} onClick={insert}>
            {t("RichText:InsertImage")}
          </Button>
        </div>
      </PopoverContent>
    </Popover>
  );
}

export function RichTextEditor({
  value,
  onChange,
  onBlur,
  id,
  readOnly = false,
  disabled = false,
  maxLength,
  "aria-invalid": ariaInvalid,
  "aria-required": ariaRequired,
  "aria-describedby": ariaDescribedBy,
  "aria-labelledby": ariaLabelledBy,
  className,
  preserveUnsupportedMarkup = true,
  transformOutput,
  ref,
}: RichTextEditorProps) {
  const { t } = useL();
  const [sourceView, setSourceView] = useState(false);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  /* The current value cannot be shown in the visual view without loss. */
  const [lossy, setLossy] = useState(false);
  const editable = !readOnly && !disabled;

  /* The last value this component emitted or applied: a `value` equal to it is an own edit (no reset). */
  const lastValue = useRef(value);
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;
  const transformRef = useRef(transformOutput);
  transformRef.current = transformOutput;
  const maxLengthRef = useRef(maxLength);
  maxLengthRef.current = maxLength;

  const [htmlCache] = useState(() => new WeakMap<PmNode, string>());
  const [initialContent] = useState(value);
  const extensions = useMemo(
    () => [
      StarterKit.configure({
        /* Links keep the attributes they have; none are added (an e-mail body, not a web page). */
        link: { openOnClick: false, HTMLAttributes: { target: null, rel: null } },
      }),
      TextAlign.configure({ types: ["heading", "paragraph"] }),
      RichTextImage,
      maxHtmlLength(() => maxLengthRef.current, htmlCache),
    ],
    [htmlCache],
  );

  const attributes = useMemo(() => {
    const attrs: Record<string, string> = {
      id,
      role: "textbox",
      "aria-multiline": "true",
      class: "rich-text-content min-h-full px-2.5 py-2 outline-none",
    };
    if (ariaLabelledBy) attrs["aria-labelledby"] = ariaLabelledBy;
    if (ariaInvalid) attrs["aria-invalid"] = "true";
    if (ariaRequired) attrs["aria-required"] = "true";
    if (ariaDescribedBy) attrs["aria-describedby"] = ariaDescribedBy;
    if (!editable) attrs["aria-readonly"] = "true";
    return attrs;
  }, [id, ariaLabelledBy, ariaInvalid, ariaRequired, ariaDescribedBy, editable]);
  const editorProps = useMemo(() => ({ attributes }), [attributes]);

  const editor = useEditor({
    extensions,
    content: initialContent,
    editable,
    editorProps,
    immediatelyRender: true,
    shouldRerenderOnTransaction: false,
    onUpdate: ({ editor: ed }) => {
      const own = htmlOf(ed, htmlCache);
      const html = transformRef.current ? transformRef.current(own) : own;
      lastValue.current = html;
      /* An edit in the visual view: the value is now what the editor holds, so the warning no longer applies. */
      setLossy(false);
      onChangeRef.current(html);
    },
  });

  /* Puts a value into the visual view without emitting and without an undo step; returns whether it was lossless. */
  const load = useCallback(
    (html: string): boolean => {
      /* useEditor destroys its instance when React unmounts or hides the component (StrictMode, Suspense) and makes a
       * new one; an effect of the old render must not touch the destroyed instance (its schema is gone). */
      if (editor.isDestroyed) return true;
      editor.chain().setMeta("addToHistory", false).setContent(html, { emitUpdate: false }).run();
      return !preserveUnsupportedMarkup || isLosslessRoundTrip(html, editor.getHTML());
    },
    [editor, preserveUnsupportedMarkup],
  );

  /* The value the editor was created with: check it once. */
  const checkedInitial = useRef(false);
  useEffect(() => {
    if (checkedInitial.current || !preserveUnsupportedMarkup || editor.isDestroyed) return;
    checkedInitial.current = true;
    if (!isLosslessRoundTrip(initialContent, editor.getHTML())) {
      setLossy(true);
      setSourceView(true);
    }
  }, [editor, initialContent, preserveUnsupportedMarkup]);

  /* A value from outside (record loaded, form reset): replace the content; a lossy one opens in the source view. */
  useEffect(() => {
    if (value === lastValue.current || editor.isDestroyed) return;
    lastValue.current = value;
    const lossless = load(value);
    setLossy(!lossless);
    if (!lossless) setSourceView(true);
  }, [editor, load, value]);

  useEffect(() => {
    if (!editor.isDestroyed) editor.setEditable(editable, false);
  }, [editor, editable]);

  const state = useEditorState({
    editor,
    selector: ({ editor: ed }): ToolState => ({
      bold: ed.isActive("bold"),
      italic: ed.isActive("italic"),
      underline: ed.isActive("underline"),
      strike: ed.isActive("strike"),
      paragraph: ed.isActive("paragraph"),
      h1: ed.isActive("heading", { level: 1 }),
      h2: ed.isActive("heading", { level: 2 }),
      h3: ed.isActive("heading", { level: 3 }),
      bulletList: ed.isActive("bulletList"),
      orderedList: ed.isActive("orderedList"),
      link: ed.isActive("link"),
      alignLeft: ed.isActive({ textAlign: "left" }),
      alignCenter: ed.isActive({ textAlign: "center" }),
      alignRight: ed.isActive({ textAlign: "right" }),
      canUndo: ed.can().undo(),
      canRedo: ed.can().redo(),
    }),
  });

  useImperativeHandle(
    ref,
    (): RichTextEditorHandle => ({
      insertText: (text) => {
        if (!editable) return;
        const textarea = textareaRef.current;
        if (sourceView) {
          if (!textarea) return;
          const start = textarea.selectionStart ?? textarea.value.length;
          const end = textarea.selectionEnd ?? start;
          const next = textarea.value.slice(0, start) + text + textarea.value.slice(end);
          /* As the textarea's own maxLength: a too-long edit is refused, silently. */
          if (maxLengthRef.current != null && next.length > maxLengthRef.current) return;
          textarea.focus();
          textarea.value = next;
          const caret = start + text.length;
          textarea.setSelectionRange(caret, caret);
          lastValue.current = next;
          onChangeRef.current(next);
          return;
        }
        if (editor.isDestroyed) return;
        /* JSON content, never a string: a string would be parsed as HTML and a tag must stay literal text. */
        const content = text.split("\n").flatMap((line, index) => [
          ...(index > 0 ? [{ type: "hardBreak" }] : []),
          ...(line ? [{ type: "text", text: line }] : []),
        ]);
        if (content.length === 0) return;
        editor.chain().focus().insertContent(content).run();
      },
      focus: () => {
        if (sourceView) textareaRef.current?.focus();
        else if (!editor.isDestroyed) editor.commands.focus();
      },
    }),
    [editable, editor, sourceView],
  );

  const toggleSource = () => {
    if (sourceView) {
      /* Back to WYSIWYG: show the source as parsed, but leave the value as typed until the user edits here. */
      setLossy(!load(value));
      lastValue.current = value;
    }
    setSourceView((s) => !s);
  };

  /* Blur is the whole control losing focus, not a click on its own toolbar. */
  const handleBlur = (e: FocusEvent<HTMLDivElement>) => {
    if (!e.currentTarget.contains(e.relatedTarget as Node | null)) onBlur?.();
  };

  const tools = !editable || sourceView;
  const run = (command: (ed: Editor) => void) => () => command(editor);

  return (
    <div
      data-slot="rich-text-editor"
      data-invalid={ariaInvalid || undefined}
      data-disabled={disabled || undefined}
      onBlur={handleBlur}
      className={cn(
        "flex w-full flex-col overflow-hidden rounded-lg border border-input bg-background transition-colors focus-within:border-ring focus-within:ring-3 focus-within:ring-ring/50 data-disabled:cursor-not-allowed data-disabled:opacity-50 data-invalid:border-destructive data-invalid:ring-3 data-invalid:ring-destructive/20",
        className,
      )}
    >
      <TooltipProvider>
        {/* pt-3 leaves room for the FormField label that sits on the top border. */}
        <div role="toolbar" aria-label={t("RichText:Toolbar")} aria-controls={id} className="flex flex-wrap items-center gap-0.5 border-b border-border px-1 pt-3 pb-1">
          <ToolButton label={t("RichText:Bold")} icon={Bold} pressed={state.bold} disabled={tools} onClick={run((ed) => ed.chain().focus().toggleBold().run())} />
          <ToolButton label={t("RichText:Italic")} icon={Italic} pressed={state.italic} disabled={tools} onClick={run((ed) => ed.chain().focus().toggleItalic().run())} />
          <ToolButton label={t("RichText:Underline")} icon={Underline} pressed={state.underline} disabled={tools} onClick={run((ed) => ed.chain().focus().toggleUnderline().run())} />
          <ToolButton label={t("RichText:Strikethrough")} icon={Strikethrough} pressed={state.strike} disabled={tools} onClick={run((ed) => ed.chain().focus().toggleStrike().run())} />
          <span aria-hidden className="mx-1 h-5 w-px bg-border" />
          <ToolButton label={t("RichText:Paragraph")} icon={Pilcrow} pressed={state.paragraph} disabled={tools} onClick={run((ed) => ed.chain().focus().setParagraph().run())} />
          <ToolButton label={t("RichText:Heading1")} icon={Heading1} pressed={state.h1} disabled={tools} onClick={run((ed) => ed.chain().focus().toggleHeading({ level: 1 }).run())} />
          <ToolButton label={t("RichText:Heading2")} icon={Heading2} pressed={state.h2} disabled={tools} onClick={run((ed) => ed.chain().focus().toggleHeading({ level: 2 }).run())} />
          <ToolButton label={t("RichText:Heading3")} icon={Heading3} pressed={state.h3} disabled={tools} onClick={run((ed) => ed.chain().focus().toggleHeading({ level: 3 }).run())} />
          <span aria-hidden className="mx-1 h-5 w-px bg-border" />
          <ToolButton label={t("RichText:BulletList")} icon={List} pressed={state.bulletList} disabled={tools} onClick={run((ed) => ed.chain().focus().toggleBulletList().run())} />
          <ToolButton label={t("RichText:OrderedList")} icon={ListOrdered} pressed={state.orderedList} disabled={tools} onClick={run((ed) => ed.chain().focus().toggleOrderedList().run())} />
          <LinkButton editor={editor} active={state.link} disabled={tools} idPrefix={id} />
          <ImageButton editor={editor} disabled={tools} idPrefix={id} />
          <span aria-hidden className="mx-1 h-5 w-px bg-border" />
          <ToolButton label={t("RichText:AlignLeft")} icon={TextAlignStart} pressed={state.alignLeft} disabled={tools} onClick={run((ed) => ed.chain().focus().setTextAlign("left").run())} />
          <ToolButton label={t("RichText:AlignCenter")} icon={TextAlignCenter} pressed={state.alignCenter} disabled={tools} onClick={run((ed) => ed.chain().focus().setTextAlign("center").run())} />
          <ToolButton label={t("RichText:AlignRight")} icon={TextAlignEnd} pressed={state.alignRight} disabled={tools} onClick={run((ed) => ed.chain().focus().setTextAlign("right").run())} />
          <span aria-hidden className="mx-1 h-5 w-px bg-border" />
          <ToolButton label={t("RichText:Undo")} icon={Undo2} disabled={tools || !state.canUndo} onClick={run((ed) => ed.chain().focus().undo().run())} />
          <ToolButton label={t("RichText:Redo")} icon={Redo2} disabled={tools || !state.canRedo} onClick={run((ed) => ed.chain().focus().redo().run())} />
          <span className="ml-auto" />
          {/* The source view stays available when read-only: viewing the HTML is not editing it. */}
          <ToolButton label={t("RichText:SourceView")} icon={CodeXml} pressed={sourceView} disabled={disabled} onClick={toggleSource} />
        </div>
      </TooltipProvider>
      {lossy && (
        <div className="px-2 pt-2">
          <Alert variant="warning" role="status">
            <TriangleAlert aria-hidden />
            <AlertDescription>{t("RichText:UnsupportedMarkupNotice")}</AlertDescription>
          </Alert>
        </div>
      )}
      {sourceView ? (
        <textarea
          ref={textareaRef}
          id={id}
          value={value}
          maxLength={maxLength}
          spellCheck={false}
          readOnly={readOnly}
          disabled={disabled}
          aria-labelledby={ariaLabelledBy}
          aria-invalid={ariaInvalid || undefined}
          aria-required={ariaRequired || undefined}
          aria-describedby={ariaDescribedBy}
          onChange={(e) => {
            lastValue.current = e.target.value;
            onChange(e.target.value);
          }}
          /* font-mono: raw technical content, HTML source (UI-TOK-008). */
          className="min-h-0 flex-1 resize-none bg-transparent px-2.5 py-2 font-mono text-label outline-none"
        />
      ) : (
        <EditorContent editor={editor} className="min-h-0 flex-1 overflow-y-auto" />
      )}
    </div>
  );
}

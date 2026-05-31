// Typed wrappers over the Tauri command surface and native dialogs.

import { invoke } from "@tauri-apps/api/core";
import { open as openDialog, save as saveDialog } from "@tauri-apps/plugin-dialog";

export interface FolderNode {
  nid: number;
  name: string;
  content_count: number;
  unread_count: number;
  children: FolderNode[];
}

export interface OpenResult {
  path: string;
  format: string;
  root_name: string;
  root: FolderNode;
}

export interface MessageSummary {
  nid: number;
  subject: string;
  sender_name: string;
  sender_email: string;
  date: number | null;
  is_read: boolean;
  has_attachments: boolean;
  size: number;
  importance: number;
}

export interface Recipient {
  name: string;
  email: string;
  kind: string;
}

export interface Attachment {
  index: number;
  filename: string;
  extension: string;
  mime: string;
  size: number;
  is_embedded: boolean;
  embedded_subject: string | null;
}

export interface MessageDetail {
  nid: number;
  subject: string;
  sender_name: string;
  sender_email: string;
  display_to: string;
  display_cc: string;
  display_bcc: string;
  message_class: string;
  date: number | null;
  creation_time: number | null;
  modification_time: number | null;
  is_read: boolean;
  importance: number;
  size: number;
  body_html: string;
  body_text: string;
  has_html: boolean;
  recipients: Recipient[];
  attachments: Attachment[];
}

export interface SearchHit {
  nid: number;
  folder_nid: number;
  folder_name: string;
  subject: string;
  sender_name: string;
  sender_email: string;
  date: number | null;
  is_read: boolean;
  has_attachments: boolean;
}

export type ExportFormat = "eml" | "txt" | "msg";

export async function pickPstFile(): Promise<string | null> {
  const selected = await openDialog({
    multiple: false,
    directory: false,
    filters: [{ name: "Outlook Data File", extensions: ["pst"] }],
  });
  return typeof selected === "string" ? selected : null;
}

export async function pickSavePath(
  defaultName: string,
  filters?: { name: string; extensions: string[] }[],
): Promise<string | null> {
  const path = await saveDialog({ defaultPath: defaultName, filters });
  return path ?? null;
}

export async function pickDirectory(): Promise<string | null> {
  const selected = await openDialog({ directory: true, multiple: false });
  return typeof selected === "string" ? selected : null;
}

export const api = {
  openPst: (path: string) => invoke<OpenResult>("open_pst", { path }),
  closePst: () => invoke<void>("close_pst"),
  listMessages: (folderNid: number) =>
    invoke<MessageSummary[]>("list_messages", { folderNid }),
  getMessage: (nid: number) => invoke<MessageDetail>("get_message", { nid }),
  saveAttachment: (msgNid: number, index: number, destPath: string) =>
    invoke<void>("save_attachment", { msgNid, index, destPath }),
  exportMessage: (nid: number, format: ExportFormat, destPath: string) =>
    invoke<void>("export_message", { nid, format, destPath }),
  exportFolder: (folderNid: number, format: ExportFormat, destDir: string) =>
    invoke<number>("export_folder", { folderNid, format, destDir }),
  search: (query: string, deep: boolean) =>
    invoke<SearchHit[]>("search", { query, deep }),
};

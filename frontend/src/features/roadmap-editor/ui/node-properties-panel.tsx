"use client";

import { ENTITY_TYPE_LABELS, NODE_TYPE_LABELS } from "@/entities/roadmap";
import type { RoadmapReactFlowNodeData } from "@/entities/roadmap";
import type {
  EntityReferenceData,
  ExternalLinkData,
  GroupData,
  TextNoteData,
} from "@/entities/roadmap";
import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { Textarea } from "@/shared/ui/kit/textarea";
import type { Node } from "@xyflow/react";
import { Link2, Trash2, X } from "lucide-react";
import { useState } from "react";

interface NodePropertiesPanelProps {
  node: Node<RoadmapReactFlowNodeData>;
  onUpdateNodeData: (nodeId: string, newRawData: string) => void;
  onDeleteNode: (nodeId: string) => void;
  onClose: () => void;
  onOpenEntityPicker: () => void;
}

export function NodePropertiesPanel({
  node,
  onUpdateNodeData,
  onDeleteNode,
  onClose,
  onOpenEntityPicker,
}: NodePropertiesPanelProps) {
  const nodeType = node.data.nodeType;

  return (
    <div className="absolute right-2 top-2 z-50 flex max-h-[calc(100%-1rem)] w-[calc(100vw-1rem)] max-w-[260px] flex-col gap-3 overflow-y-auto rounded-xl border bg-card p-4 shadow-lg sm:max-w-[280px]">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold">{NODE_TYPE_LABELS[nodeType] ?? nodeType}</h3>
        <Button variant="ghost" size="icon" className="size-7" onClick={onClose}>
          <X className="size-4" />
        </Button>
      </div>

      {nodeType === "EntityReference" && (
        <EntityReferenceForm key={node.id} node={node} onOpenEntityPicker={onOpenEntityPicker} />
      )}
      {nodeType === "TextNote" && (
        <TextNoteForm key={node.id} node={node} onUpdate={onUpdateNodeData} />
      )}
      {nodeType === "ExternalLink" && (
        <ExternalLinkForm key={node.id} node={node} onUpdate={onUpdateNodeData} />
      )}
      {nodeType === "Group" && <GroupForm key={node.id} node={node} onUpdate={onUpdateNodeData} />}

      <Button
        variant="destructive"
        size="sm"
        className="mt-2 gap-1.5"
        onClick={() => onDeleteNode(node.id)}
      >
        <Trash2 className="size-3.5" />
        Удалить элемент
      </Button>
    </div>
  );
}

function EntityReferenceForm({
  node,
  onOpenEntityPicker,
}: {
  node: Node<RoadmapReactFlowNodeData>;
  onOpenEntityPicker: () => void;
}) {
  const parsed: EntityReferenceData = JSON.parse(node.data.rawData);
  const isLinked = !!parsed.entityId;

  return (
    <div className="flex flex-col gap-2.5">
      {isLinked ? (
        <div className="rounded-lg border bg-muted/30 p-2.5">
          <p className="text-2xs font-medium text-muted-foreground">
            {ENTITY_TYPE_LABELS[parsed.entityType] ?? parsed.entityType}
          </p>
          <p className="mt-0.5 text-sm font-semibold">{parsed.entityTitle}</p>
          {parsed.entityDescription && (
            <p className="mt-0.5 line-clamp-2 text-2xs text-muted-foreground">
              {parsed.entityDescription}
            </p>
          )}
        </div>
      ) : (
        <div className="rounded-lg border border-dashed bg-muted/10 p-3 text-center">
          <p className="text-xs text-muted-foreground">Не привязан к материалу</p>
        </div>
      )}

      <Button variant="outline" size="sm" className="gap-1.5" onClick={onOpenEntityPicker}>
        <Link2 className="size-3.5" />
        {isLinked ? "Изменить привязку" : "Привязать к материалу"}
      </Button>
    </div>
  );
}

function TextNoteForm({
  node,
  onUpdate,
}: {
  node: Node<RoadmapReactFlowNodeData>;
  onUpdate: (nodeId: string, data: string) => void;
}) {
  const parsed: TextNoteData = JSON.parse(node.data.rawData);
  const [text, setText] = useState(parsed.text);

  function save() {
    onUpdate(node.id, JSON.stringify({ ...parsed, text }));
  }

  return (
    <div>
      <Label className="text-xs">Текст</Label>
      <Textarea
        className="mt-1 min-h-[80px]"
        value={text}
        onChange={(e) => setText(e.target.value)}
        onBlur={save}
        placeholder="Текст заметки..."
      />
    </div>
  );
}

function ExternalLinkForm({
  node,
  onUpdate,
}: {
  node: Node<RoadmapReactFlowNodeData>;
  onUpdate: (nodeId: string, data: string) => void;
}) {
  const parsed: ExternalLinkData = JSON.parse(node.data.rawData);
  const [title, setTitle] = useState(parsed.title);
  const [url, setUrl] = useState(parsed.url);
  const [description, setDescription] = useState(parsed.description ?? "");

  function save() {
    onUpdate(node.id, JSON.stringify({ url, title, description: description || undefined }));
  }

  return (
    <div className="flex flex-col gap-2.5">
      <div>
        <Label className="text-xs">Название</Label>
        <Input
          className="mt-1"
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          onBlur={save}
          placeholder="Название"
        />
      </div>
      <div>
        <Label className="text-xs">URL</Label>
        <Input
          className="mt-1"
          value={url}
          onChange={(e) => setUrl(e.target.value)}
          onBlur={save}
          placeholder="https://..."
        />
      </div>
      <div>
        <Label className="text-xs">Описание</Label>
        <Input
          className="mt-1"
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          onBlur={save}
          placeholder="Описание"
        />
      </div>
    </div>
  );
}

function GroupForm({
  node,
  onUpdate,
}: {
  node: Node<RoadmapReactFlowNodeData>;
  onUpdate: (nodeId: string, data: string) => void;
}) {
  const parsed: GroupData = JSON.parse(node.data.rawData);
  const [label, setLabel] = useState(parsed.label);

  function save() {
    onUpdate(node.id, JSON.stringify({ ...parsed, label }));
  }

  return (
    <div>
      <Label className="text-xs">Название группы</Label>
      <Input
        className="mt-1"
        value={label}
        onChange={(e) => setLabel(e.target.value)}
        onBlur={save}
        placeholder="Название"
      />
    </div>
  );
}

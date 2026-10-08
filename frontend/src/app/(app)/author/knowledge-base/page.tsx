"use client";

import { MaterialsPage } from "@/features/materials-browse";
import { useDeleteMaterial } from "@/features/materials-manage";

export default function AuthorMaterialsPage() {
  const { deleteMaterial, isPending } = useDeleteMaterial();

  return (
    <MaterialsPage
      mode="teaching"
      onDeleteMaterial={(id) => void deleteMaterial(id)}
      isDeletePending={isPending}
    />
  );
}

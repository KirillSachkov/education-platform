import { certificateQueryOptions, certificatesApi } from "@/entities/certificate";
import { getErrorMessage } from "@/shared/api";
import { routes } from "@/shared/config/routes";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { toast } from "sonner";

/**
 * Claim сертификата о прохождении курса (#467). Бэкенд идемпотентен — если
 * сертификат уже выдан, возвращается существующий, поэтому редирект на публичную
 * страницу единый для обоих случаев.
 */
export function useClaimCertificate(courseId: string) {
  const queryClient = useQueryClient();
  const router = useRouter();

  return useMutation({
    mutationFn: () => certificatesApi.claimCertificate(courseId),
    onSuccess: async (certificate) => {
      toast.success("Сертификат получен");
      await queryClient.invalidateQueries({ queryKey: [certificateQueryOptions.baseKey] });
      router.push(routes.certificates(certificate.id));
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось получить сертификат")),
  });
}

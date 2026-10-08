import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type { CourseCertificateDto } from "./types";

export const certificatesApi = {
  /** Идемпотентный claim: при повторном вызове бэкенд возвращает уже выданный сертификат. */
  claimCertificate: async (courseId: string) => {
    const res = await apiClient.post<Envelope<CourseCertificateDto>>(
      `/progress/courses/${courseId}/certificate/claim/`,
    );
    return res.data.result!;
  },

  getMyCertificates: async ({ signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<CourseCertificateDto[]>>(
      `/progress/certificates/my/`,
      { signal },
    );
    return res.data.result ?? [];
  },
};

export const certificateQueryOptions = {
  baseKey: "certificates",

  myKey: () => [certificateQueryOptions.baseKey, "my"] as const,

  myOptions: () =>
    queryOptions({
      queryKey: certificateQueryOptions.myKey(),
      queryFn: ({ signal }) => certificatesApi.getMyCertificates({ signal }),
      staleTime: 60_000,
    }),
};

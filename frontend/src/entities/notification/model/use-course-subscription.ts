import { notificationQueryOptions, notificationsApi } from "../api";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Управляет подпиской текущего пользователя на курс через NotificationService.
 * Подписчики получают уведомления о новых материалах/заданиях курса (InApp +
 * Telegram, если канал привязан и включён).
 *
 * При зачислении на курс подписка создаётся автоматически в `CourseEnrolledHandler`
 * на стороне NotificationService — этот хук нужен для **toggle** на странице курса
 * (отписаться, если уже подписан; подписаться, если ещё нет).
 */
export function useCourseSubscription(courseId: string) {
  const queryClient = useQueryClient();

  const subscriptionsQuery = useQuery(notificationQueryOptions.subscriptions());
  const subscription = subscriptionsQuery.data?.find(
    (s) => s.entityType === "course" && s.entityId === courseId,
  );

  const subscribeMutation = useMutation({
    mutationFn: () =>
      notificationsApi.subscribe({ entityType: "course", entityId: courseId }),
    onSuccess: async () => {
      toast.success("Вы подписались на курс");
      await queryClient.invalidateQueries({
        queryKey: notificationQueryOptions.subscriptionsKey(),
      });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка подписки"));
    },
  });

  const unsubscribeMutation = useMutation({
    mutationFn: (subscriptionId: string) =>
      notificationsApi.unsubscribe(subscriptionId),
    onSuccess: async () => {
      toast.success("Вы отписались от курса");
      await queryClient.invalidateQueries({
        queryKey: notificationQueryOptions.subscriptionsKey(),
      });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка отписки"));
    },
  });

  return {
    isSubscribed: !!subscription,
    subscriptionId: subscription?.id ?? null,
    isLoading: subscriptionsQuery.isLoading,
    isPending: subscribeMutation.isPending || unsubscribeMutation.isPending,
    subscribe: () => subscribeMutation.mutateAsync(),
    unsubscribe: () => {
      if (subscription) return unsubscribeMutation.mutateAsync(subscription.id);
      return Promise.resolve();
    },
  };
}

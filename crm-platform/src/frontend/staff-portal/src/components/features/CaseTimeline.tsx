import { useTranslation } from 'react-i18next';
import type { Case, CaseComment } from '../../types';
import { Badge } from '../ui';
import { useAuthStore } from '../../stores/authStore';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { zodResolver } from '@hookform/resolvers/zod';
import { useAddCaseComment } from '../../hooks/useCases';
import { Button, Input } from '../ui';

interface CaseTimelineProps {
  caseItem: Case;
  comments: CaseComment[];
}

const commentSchema = z.object({
  body: z.string().min(1),
  isInternal: z.boolean(),
});

type CommentFormValues = z.infer<typeof commentSchema>;

function formatDate(iso: string) {
  return new Intl.DateTimeFormat('en-US', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(iso));
}

function getSentimentVariant(sentiment?: Case['sentiment']) {
  switch (sentiment) {
    case 'Positive': return 'success' as const;
    case 'Negative': return 'danger' as const;
    case 'Mixed':    return 'warning' as const;
    default:         return 'default' as const;
  }
}

export function CaseTimeline({ caseItem, comments }: CaseTimelineProps) {
  const { t } = useTranslation();
  const user = useAuthStore((s) => s.user);
  const addComment = useAddCaseComment(caseItem.id);

  const { register, handleSubmit, reset, formState: { errors } } = useForm<CommentFormValues>({
    resolver: zodResolver(commentSchema),
    defaultValues: { body: '', isInternal: false },
  });

  const onSubmit = (values: CommentFormValues) => {
    addComment.mutate(values, { onSuccess: () => { reset(); } });
  };

  return (
    <div className="space-y-6">
      {/* AI summary + sentiment */}
      {(caseItem.aiSummary ?? caseItem.sentiment) && (
        <div className="rounded-lg border border-primary-200 bg-primary-50 p-4">
          {caseItem.aiSummary && (
            <p className="text-sm text-gray-700">{caseItem.aiSummary}</p>
          )}
          {caseItem.sentiment && (
            <div className="mt-2">
              <Badge label={caseItem.sentiment} variant={getSentimentVariant(caseItem.sentiment)} />
            </div>
          )}
        </div>
      )}

      {/* Comment list */}
      <div className="space-y-3">
        {comments.map((comment) => (
          <div
            key={comment.id}
            className={[
              'rounded-lg border p-4',
              comment.isInternal
                ? 'border-warning-200 bg-warning-50'
                : 'border-gray-200 bg-white',
            ].join(' ')}
          >
            <div className="mb-1 flex items-center justify-between">
              <span className="text-sm font-medium text-gray-800">{comment.authorName}</span>
              <span className="text-xs text-gray-400">{formatDate(comment.createdAt)}</span>
            </div>
            <p className="text-sm text-gray-700 whitespace-pre-line">{comment.body}</p>
            {comment.isInternal && (
              <span className="mt-1 text-xs italic text-warning-700">{t('cases.timeline')} — internal</span>
            )}
          </div>
        ))}

        {comments.length === 0 && (
          <p className="text-sm text-gray-400">{t('common.noResults')}</p>
        )}
      </div>

      {/* Add comment */}
      {user && (
        <form onSubmit={(e) => { void handleSubmit(onSubmit)(e); }} className="space-y-3">
          <Input
            label={t('cases.addComment')}
            {...register('body')}
            error={errors.body?.message}
          />
          <div className="flex items-center gap-3">
            <label className="flex items-center gap-2 text-sm text-gray-600">
              <input type="checkbox" {...register('isInternal')} className="rounded" />
              Internal note
            </label>
            <Button type="submit" loading={addComment.isPending} size="sm">
              {t('cases.addComment')}
            </Button>
          </div>
        </form>
      )}
    </div>
  );
}

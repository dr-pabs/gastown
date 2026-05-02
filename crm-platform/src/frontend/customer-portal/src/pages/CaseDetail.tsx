import { useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useCase, useCaseComments, useAddCaseComment, useCloseCase } from '../hooks/useCases';
import { Badge, Button, Spinner } from '../components/ui';
import type { CaseStatus, CasePriority, SentimentLabel } from '../types';

const statusVariant: Record<CaseStatus, 'default' | 'info' | 'success' | 'warning' | 'danger'> = {
  Open: 'info',
  InProgress: 'warning',
  WaitingOnCustomer: 'default',
  Resolved: 'success',
  Closed: 'default',
};

const priorityVariant: Record<CasePriority, 'default' | 'info' | 'success' | 'warning' | 'danger'> = {
  Low: 'default',
  Medium: 'info',
  High: 'warning',
  Critical: 'danger',
};

const sentimentVariant: Record<SentimentLabel, 'default' | 'success' | 'danger' | 'warning'> = {
  Positive: 'success',
  Neutral: 'default',
  Negative: 'danger',
  Mixed: 'warning',
};

export function CaseDetail() {
  const { t } = useTranslation();
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [commentBody, setCommentBody] = useState('');
  const [showCloseConfirm, setShowCloseConfirm] = useState(false);

  const { data: caseItem, isLoading: caseLoading } = useCase(id ?? '');
  const { data: comments, isLoading: commentsLoading } = useCaseComments(id ?? '');
  const addComment = useAddCaseComment(id ?? '');
  const closeCase = useCloseCase(id ?? '');

  const isClosed = caseItem?.status === 'Closed' || caseItem?.status === 'Resolved';

  const handleAddComment = async () => {
    if (!commentBody.trim()) return;
    await addComment.mutateAsync({ body: commentBody.trim() });
    setCommentBody('');
  };

  const handleClose = async () => {
    await closeCase.mutateAsync(undefined);
    setShowCloseConfirm(false);
  };

  if (caseLoading) {
    return (
      <div className="flex h-64 items-center justify-center">
        <Spinner size="lg" />
      </div>
    );
  }

  if (!caseItem) {
    return (
      <div className="mx-auto max-w-4xl px-4 py-8 text-center text-gray-500">
        {t('errors.notFound')}
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-5xl px-4 py-8">
      {/* Back link */}
      <button
        type="button"
        onClick={() => { void navigate(-1 as Parameters<typeof navigate>[0]); }}
        className="mb-4 text-sm text-blue-600 hover:underline"
      >
        ← {t('common.back')}
      </button>

      {/* SLA breach banner */}
      {caseItem.slaBreached && (
        <div className="mb-4 rounded-md bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
          ⚠ {t('cases.slaBreached')}
        </div>
      )}

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
        {/* Main column */}
        <div className="space-y-6 lg:col-span-2">
          {/* Case header */}
          <div className="rounded-lg border border-gray-200 bg-white p-6 shadow-sm">
            <div className="mb-3 flex items-start justify-between gap-4">
              <h1 className="text-xl font-bold text-gray-900">{caseItem.subject}</h1>
              {!isClosed && (
                <Button
                  variant="danger"
                  size="sm"
                  onClick={() => { setShowCloseConfirm(true); }}
                >
                  {t('cases.closeCase')}
                </Button>
              )}
            </div>
            {caseItem.description && (
              <p className="whitespace-pre-wrap text-sm text-gray-700">{caseItem.description}</p>
            )}
          </div>

          {/* Comment thread */}
          <div className="rounded-lg border border-gray-200 bg-white shadow-sm">
            <div className="border-b border-gray-100 px-5 py-4">
              <h2 className="font-semibold text-gray-900">{t('cases.comments')}</h2>
            </div>

            {commentsLoading ? (
              <div className="flex h-16 items-center justify-center">
                <Spinner size="sm" />
              </div>
            ) : (
              <div className="divide-y divide-gray-50">
                {(comments ?? [])
                  .filter((c) => !c.isInternal)
                  .map((comment) => (
                    <div key={comment.id} className="px-5 py-4">
                      <div className="mb-1 flex items-center justify-between">
                        <span className="text-sm font-medium text-gray-900">
                          {comment.authorName ?? t('common.unknown')}
                        </span>
                        <span className="text-xs text-gray-400">
                          {new Date(comment.createdAt).toLocaleString()}
                        </span>
                      </div>
                      <p className="text-sm text-gray-700">{comment.body}</p>
                    </div>
                  ))}
                {(comments ?? []).filter((c) => !c.isInternal).length === 0 && (
                  <p className="px-5 py-6 text-center text-sm text-gray-400">
                    {t('cases.noComments')}
                  </p>
                )}
              </div>
            )}

            {/* New comment form */}
            {!isClosed && (
              <div className="border-t border-gray-100 px-5 py-4">
                <textarea
                  value={commentBody}
                  onChange={(e) => { setCommentBody(e.target.value); }}
                  rows={3}
                  placeholder={t('cases.commentPlaceholder')}
                  className="block w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                />
                <div className="mt-2 flex justify-end">
                  <Button
                    size="sm"
                    onClick={() => { void handleAddComment(); }}
                    loading={addComment.isPending}
                    disabled={!commentBody.trim()}
                  >
                    {t('cases.postComment')}
                  </Button>
                </div>
              </div>
            )}
          </div>
        </div>

        {/* Sidebar */}
        <div className="space-y-4">
          <div className="rounded-lg border border-gray-200 bg-white p-5 shadow-sm">
            <h3 className="mb-4 text-sm font-semibold uppercase tracking-wide text-gray-500">
              {t('cases.details')}
            </h3>
            <dl className="space-y-3 text-sm">
              <div>
                <dt className="text-gray-500">{t('cases.status')}</dt>
                <dd className="mt-1">
                  <Badge label={caseItem.status} variant={statusVariant[caseItem.status]} />
                </dd>
              </div>
              <div>
                <dt className="text-gray-500">{t('cases.priority')}</dt>
                <dd className="mt-1">
                  <Badge label={caseItem.priority} variant={priorityVariant[caseItem.priority]} />
                </dd>
              </div>
              {caseItem.category && (
                <div>
                  <dt className="text-gray-500">{t('cases.category')}</dt>
                  <dd className="mt-1 text-gray-900">{caseItem.category}</dd>
                </div>
              )}
              {caseItem.sentiment && (
                <div>
                  <dt className="text-gray-500">{t('cases.sentiment')}</dt>
                  <dd className="mt-1">
                    <Badge label={caseItem.sentiment} variant={sentimentVariant[caseItem.sentiment]} />
                  </dd>
                </div>
              )}
              <div>
                <dt className="text-gray-500">{t('cases.createdAt')}</dt>
                <dd className="mt-1 text-gray-900">
                  {new Date(caseItem.createdAt).toLocaleDateString()}
                </dd>
              </div>
              {caseItem.slaDueAt && (
                <div>
                  <dt className="text-gray-500">{t('cases.slaDue')}</dt>
                  <dd className={`mt-1 font-medium ${caseItem.slaBreached ? 'text-red-600' : 'text-gray-900'}`}>
                    {new Date(caseItem.slaDueAt).toLocaleString()}
                  </dd>
                </div>
              )}
              {caseItem.resolvedAt && (
                <div>
                  <dt className="text-gray-500">{t('cases.resolvedAt')}</dt>
                  <dd className="mt-1 text-gray-900">
                    {new Date(caseItem.resolvedAt).toLocaleDateString()}
                  </dd>
                </div>
              )}
            </dl>
          </div>
        </div>
      </div>

      {/* Close confirm dialog */}
      {showCloseConfirm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="w-full max-w-sm rounded-lg bg-white p-6 shadow-xl">
            <h2 className="mb-2 text-lg font-semibold text-gray-900">{t('cases.closeCaseTitle')}</h2>
            <p className="mb-6 text-sm text-gray-600">{t('cases.closeCaseConfirm')}</p>
            <div className="flex gap-3 justify-end">
              <Button variant="secondary" onClick={() => { setShowCloseConfirm(false); }}>
                {t('common.cancel')}
              </Button>
              <Button
                variant="danger"
                loading={closeCase.isPending}
                onClick={() => { void handleClose(); }}
              >
                {t('cases.closeCase')}
              </Button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

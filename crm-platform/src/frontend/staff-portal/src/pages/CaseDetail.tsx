import { useParams, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useCase } from '../hooks/useCases';
import { Badge, Button, Spinner } from '../components/ui';
import { CaseTimeline } from '../components/features/CaseTimeline';
import { AiDraftComposer } from '../components/features/AiDraftComposer';
import type { CasePriority } from '../types';

const priorityVariant: Record<CasePriority, 'default' | 'info' | 'warning' | 'danger' | 'success'> = {
  Low: 'default',
  Medium: 'info',
  High: 'warning',
  Critical: 'danger',
};

export function CaseDetail() {
  const { id } = useParams<{ id: string }>();
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { data: caseItem, isLoading } = useCase(id ?? '');

  if (isLoading) {
    return <div className="flex h-64 items-center justify-center"><Spinner size="lg" /></div>;
  }
  if (!caseItem) {
    return <div className="p-6"><p className="text-gray-500">{t('errors.notFound')}</p></div>;
  }

  return (
    <div className="p-6 max-w-5xl mx-auto">
      <div className="mb-6 flex items-center gap-4">
        <button onClick={() => { void navigate('/cases'); }} className="text-sm text-gray-500 hover:text-gray-700">
          {`\u2190 ${t('cases.title')}`}
        </button>
        <h1 className="text-2xl font-bold text-gray-900 flex-1">{caseItem.subject}</h1>
        <Badge label={caseItem.status} variant={caseItem.status === 'Closed' ? 'success' : 'info'} />
        <Badge label={caseItem.priority} variant={priorityVariant[caseItem.priority]} />
      </div>

      {caseItem.slaBreached && (
        <div className="mb-4 rounded-md border border-danger-200 bg-danger-50 px-4 py-3 text-sm text-danger-700">
          {t('cases.slaBreachWarning')}
          {caseItem.slaDueAt && ` (${new Date(caseItem.slaDueAt).toLocaleString()})`}
        </div>
      )}

      <div className="grid grid-cols-3 gap-6">
        <div className="col-span-2">
          <CaseTimeline caseId={caseItem.id} />
        </div>

        <div className="space-y-4">
          <div className="rounded-lg border border-gray-200 bg-white p-4">
            <h3 className="mb-3 text-sm font-semibold text-gray-700">{t('cases.details')}</h3>
            <dl className="space-y-2 text-sm">
              <div>
                <dt className="text-gray-500">{t('cases.status')}</dt>
                <dd className="font-medium">{caseItem.status}</dd>
              </div>
              <div>
                <dt className="text-gray-500">{t('cases.priority')}</dt>
                <dd className="font-medium">{caseItem.priority}</dd>
              </div>
              {caseItem.sentiment && (
                <div>
                  <dt className="text-gray-500">{t('cases.sentiment')}</dt>
                  <dd className="font-medium">{caseItem.sentiment}</dd>
                </div>
              )}
              {caseItem.slaDueAt && (
                <div>
                  <dt className="text-gray-500">{t('cases.slaDeadline')}</dt>
                  <dd className="font-medium">{new Date(caseItem.slaDueAt).toLocaleString()}</dd>
                </div>
              )}
              <div>
                <dt className="text-gray-500">{t('common.created')}</dt>
                <dd>{new Date(caseItem.createdAt).toLocaleDateString()}</dd>
              </div>
            </dl>
          </div>

          <div className="rounded-lg border border-gray-200 bg-white p-4">
            <h3 className="mb-3 text-sm font-semibold text-gray-700">{t('ai.draftReply')}</h3>
            <AiDraftComposer
              entityType="case"
              entityId={caseItem.id}
              onInsert={(draft) => { console.info('Draft ready', draft.substring(0, 20)); }}
            />
          </div>

          {caseItem.status !== 'Closed' && (
            <Button variant="secondary" className="w-full">
              {t('cases.closeCase')}
            </Button>
          )}
        </div>
      </div>
    </div>
  );
}

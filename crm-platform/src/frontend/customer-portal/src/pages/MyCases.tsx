import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useMyCases } from '../hooks/useCases';
import { Badge, Button, Spinner } from '../components/ui';
import type { Case, CaseStatus, CasePriority, PaginationParams } from '../types';

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

export function MyCases() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [params, setParams] = useState<PaginationParams>({ page: 1, pageSize: 20 });

  const { data, isLoading } = useMyCases(params);
  const cases = data?.items ?? [];

  return (
    <div className="mx-auto max-w-5xl px-4 py-8">
      <div className="mb-6 flex items-center justify-between">
        <h1 className="text-2xl font-bold text-gray-900">{t('cases.myCasesTitle')}</h1>
        <Button onClick={() => { void navigate('/cases/new'); }}>{t('cases.newCase')}</Button>
      </div>

      {isLoading ? (
        <div className="flex h-32 items-center justify-center">
          <Spinner size="lg" />
        </div>
      ) : cases.length === 0 ? (
        <div className="rounded-lg border border-dashed border-gray-300 bg-white py-12 text-center">
          <p className="text-sm text-gray-400">{t('cases.noActiveCases')}</p>
          <Button className="mt-4" size="sm" onClick={() => { void navigate('/cases/new'); }}>
            {t('cases.newCase')}
          </Button>
        </div>
      ) : (
        <div className="overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
          <table className="w-full text-sm">
            <thead className="border-b border-gray-200 bg-gray-50">
              <tr>
                <th className="px-4 py-3 text-left font-medium text-gray-600">{t('cases.subject')}</th>
                <th className="px-4 py-3 text-left font-medium text-gray-600">{t('cases.status')}</th>
                <th className="px-4 py-3 text-left font-medium text-gray-600">{t('cases.priority')}</th>
                <th className="px-4 py-3 text-left font-medium text-gray-600">{t('cases.createdAt')}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {cases.map((c: Case) => (
                <tr
                  key={c.id}
                  onClick={() => { void navigate(`/cases/${c.id}`); }}
                  className="cursor-pointer hover:bg-gray-50 transition-colors"
                >
                  <td className="px-4 py-3 font-medium text-gray-900">{c.subject}</td>
                  <td className="px-4 py-3">
                    <Badge label={c.status} variant={statusVariant[c.status]} />
                  </td>
                  <td className="px-4 py-3">
                    <Badge label={c.priority} variant={priorityVariant[c.priority]} />
                  </td>
                  <td className="px-4 py-3 text-gray-500">
                    {new Date(c.createdAt).toLocaleDateString()}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {data && data.totalPages > 1 && (
            <div className="flex justify-end gap-2 border-t border-gray-100 px-4 py-3">
              <Button size="sm" variant="secondary" disabled={params.page <= 1}
                onClick={() => { setParams((p) => ({ ...p, page: p.page - 1 })); }}>
                {t('common.back')}
              </Button>
              <Button size="sm" variant="secondary" disabled={params.page >= data.totalPages}
                onClick={() => { setParams((p) => ({ ...p, page: p.page + 1 })); }}>
                Next
              </Button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

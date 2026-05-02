import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useJourneys, useDeleteJourney } from '../hooks/useJourneys';
import { Table, Button, Badge, ConfirmModal } from '../components/ui';
import type { Column } from '../components/ui';
import type { Journey, JourneyStatus, PaginationParams } from '../types';

// JourneyStatus = 'Draft' | 'Active' | 'Paused' | 'Archived'
const statusVariant: Record<JourneyStatus, 'default' | 'info' | 'success' | 'warning' | 'danger'> = {
  Draft: 'default',
  Active: 'success',
  Paused: 'warning',
  Archived: 'default',
};

export function Journeys() {
  const { t } = useTranslation();
  const [params, setParams] = useState<PaginationParams>({ page: 1, pageSize: 20 });
  const [deleteId, setDeleteId] = useState<string | null>(null);

  const { data, isLoading } = useJourneys(params);
  const deleteJourney = useDeleteJourney();

  const columns: Column<Journey>[] = [
    { key: 'name', header: t('journeys.name') },
    {
      key: 'status',
      header: t('journeys.status'),
      render: (row) => <Badge label={row.status} variant={statusVariant[row.status]} />,
    },
    {
      key: 'enrollmentCount',
      header: t('journeys.enrolled'),
      render: (row) => row.enrollmentCount.toLocaleString(),
    },
    {
      key: 'activeEnrollments',
      header: t('journeys.active'),
      render: (row) => row.activeEnrollments.toLocaleString(),
    },
    {
      key: 'completedEnrollments',
      header: t('journeys.completed'),
      render: (row) => row.completedEnrollments.toLocaleString(),
    },
    {
      key: 'createdAt',
      header: t('common.created'),
      render: (row) => new Date(row.createdAt).toLocaleDateString(),
    },
    {
      key: 'actions',
      header: t('common.actions'),
      render: (row) => (
        <button
          onClick={(e: { stopPropagation: () => void }) => { e.stopPropagation(); setDeleteId(row.id); }}
          className="text-xs text-red-500 hover:text-red-700"
        >
          {t('common.delete')}
        </button>
      ),
    },
  ];

  return (
    <div className="p-6">
      <div className="mb-6 flex items-center justify-between">
        <h1 className="text-2xl font-bold text-gray-900">{t('journeys.title')}</h1>
      </div>

      <Table
        columns={columns}
        data={data?.items ?? []}
        loading={isLoading}
        emptyMessage={t('common.noResults')}
      />

      {data && data.totalPages > 1 && (
        <div className="mt-4 flex justify-end gap-2">
          <Button
            size="sm"
            variant="secondary"
            disabled={params.page <= 1}
            onClick={() => { setParams((p: PaginationParams) => ({ ...p, page: p.page - 1 })); }}
          >{t('common.previous')}</Button>
          <Button
            size="sm"
            variant="secondary"
            disabled={params.page >= data.totalPages}
            onClick={() => { setParams((p: PaginationParams) => ({ ...p, page: p.page + 1 })); }}
          >{t('common.next')}</Button>
        </div>
      )}

      <ConfirmModal
        open={deleteId !== null}
        onClose={() => { setDeleteId(null); }}
        onConfirm={() => {
          if (deleteId) deleteJourney.mutate(deleteId, { onSuccess: () => { setDeleteId(null); } });
        }}
        title={t('journeys.deleteJourney')}
        message={t('journeys.deleteConfirm')}
        confirmLabel={t('common.delete')}
        loading={deleteJourney.isPending}
      />
    </div>
  );
}

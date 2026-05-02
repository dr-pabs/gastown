import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useCampaigns, useDeleteCampaign } from '../hooks/useCampaigns';
import { Table, Button, Badge, ConfirmModal } from '../components/ui';
import type { Column } from '../components/ui';
import type { Campaign, CampaignStatus, PaginationParams } from '../types';

const statusVariant: Record<CampaignStatus, 'default' | 'info' | 'success' | 'warning' | 'danger'> = {
  Draft: 'default',
  Active: 'success',
  Paused: 'warning',
  Completed: 'info',
  Archived: 'default',
};

export function Campaigns() {
  const { t } = useTranslation();
  const [params, setParams] = useState<PaginationParams>({ page: 1, pageSize: 20 });
  const [deleteId, setDeleteId] = useState<string | null>(null);

  const { data, isLoading } = useCampaigns(params);
  const deleteCampaign = useDeleteCampaign();

  const columns: Column<Campaign>[] = [
    { key: 'name', header: t('campaigns.name') },
    {
      key: 'status',
      header: t('campaigns.status'),
      render: (row) => <Badge label={row.status} variant={statusVariant[row.status]} />,
    },
    {
      key: 'impressions',
      header: t('campaigns.impressions'),
      render: (row) => row.impressions.toLocaleString(),
    },
    {
      key: 'clicks',
      header: t('campaigns.clicks'),
      render: (row) => row.clicks.toLocaleString(),
    },
    {
      key: 'conversions',
      header: t('campaigns.conversions'),
      render: (row) => row.conversions.toLocaleString(),
    },
    {
      key: 'startDate',
      header: t('campaigns.startDate'),
      render: (row) => (row.startDate ? new Date(row.startDate).toLocaleDateString() : '\u2014'),
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
        <h1 className="text-2xl font-bold text-gray-900">{t('campaigns.title')}</h1>
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
          if (deleteId) deleteCampaign.mutate(deleteId, { onSuccess: () => { setDeleteId(null); } });
        }}
        title={t('campaigns.deleteCampaign')}
        message={t('campaigns.deleteConfirm')}
        confirmLabel={t('common.delete')}
        loading={deleteCampaign.isPending}
      />
    </div>
  );
}

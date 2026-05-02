import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { zodResolver } from '@hookform/resolvers/zod';
import { useCases, useCreateCase, useDeleteCase } from '../hooks/useCases';
import { Table, Button, Input, Modal, ConfirmModal, Badge } from '../components/ui';
import type { Column } from '../components/ui';
import type { Case, CreateCaseRequest, PaginationParams, CasePriority } from '../types';

const createCaseSchema = z.object({
  subject: z.string().min(1),
  description: z.string().optional(),
  priority: z.enum(['Low', 'Medium', 'High', 'Critical']),
});

type CreateCaseFormValues = z.infer<typeof createCaseSchema>;

const priorityVariant: Record<CasePriority, 'default' | 'info' | 'warning' | 'danger' | 'success'> = {
  Low: 'default',
  Medium: 'info',
  High: 'warning',
  Critical: 'danger',
};

export function Cases() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [params, setParams] = useState<PaginationParams>({ page: 1, pageSize: 20 });
  const [createOpen, setCreateOpen] = useState(false);
  const [deleteId, setDeleteId] = useState<string | null>(null);

  const { data, isLoading } = useCases(params);
  const createCase = useCreateCase();
  const deleteCase = useDeleteCase();

  const { register, handleSubmit, reset, formState: { errors } } = useForm<CreateCaseFormValues>({
    resolver: zodResolver(createCaseSchema),
    defaultValues: { priority: 'Medium' },
  });

  const onCreateSubmit = (values: CreateCaseFormValues) => {
    const req: CreateCaseRequest = values;
    createCase.mutate(req, {
      onSuccess: () => { setCreateOpen(false); reset(); },
    });
  };

  const columns: Column<Case>[] = [
    {
      key: 'subject',
      header: t('cases.subject'),
      render: (row) => (
        <span className="flex items-center gap-2">
          {row.subject}
          {row.slaBreached && <Badge label={t('cases.slaBreach')} variant="danger" />}
        </span>
      ),
    },
    {
      key: 'status',
      header: t('cases.status'),
      render: (row) => <Badge label={row.status} variant={row.status === 'Closed' ? 'success' : 'info'} />,
    },
    {
      key: 'priority',
      header: t('cases.priority'),
      render: (row) => <Badge label={row.priority} variant={priorityVariant[row.priority]} />,
    },
    {
      key: 'sentiment',
      header: t('cases.sentiment'),
      render: (row) => row.sentiment ?? '—',
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
          className="text-xs text-danger-500 hover:text-danger-700"
        >
          {t('common.delete')}
        </button>
      ),
    },
  ];

  return (
    <div className="p-6">
      <div className="mb-6 flex items-center justify-between">
        <h1 className="text-2xl font-bold text-gray-900">{t('cases.title')}</h1>
        <Button onClick={() => { setCreateOpen(true); }}>{t('cases.newCase')}</Button>
      </div>

      <Table
        columns={columns}
        data={data?.items ?? []}
        loading={isLoading}
        emptyMessage={t('common.noResults')}
        onRowClick={(row) => { void navigate(`/cases/${row.id}`); }}
      />

      {data && data.totalPages > 1 && (
        <div className="mt-4 flex justify-end gap-2">
          <Button size="sm" variant="secondary" disabled={params.page <= 1}
            onClick={() => { setParams((p: PaginationParams) => ({ ...p, page: p.page - 1 })); }}>
            {t('common.previous')}
          </Button>
          <Button size="sm" variant="secondary" disabled={params.page >= data.totalPages}
            onClick={() => { setParams((p: PaginationParams) => ({ ...p, page: p.page + 1 })); }}>
            {t('common.next')}
          </Button>
        </div>
      )}

      <Modal
        open={createOpen}
        onClose={() => { setCreateOpen(false); reset(); }}
        title={t('cases.newCase')}
        footer={
          <>
            <Button variant="secondary" onClick={() => { setCreateOpen(false); reset(); }}>
              {t('common.cancel')}
            </Button>
            <Button form="create-case-form" type="submit" loading={createCase.isPending}>
              {t('common.create')}
            </Button>
          </>
        }
      >
        <form
          id="create-case-form"
          onSubmit={(e) => { void handleSubmit(onCreateSubmit)(e); }}
          className="space-y-4"
        >
          <Input label={t('cases.subject')} required {...register('subject')} error={errors.subject?.message} />
          <div>
            <label className="mb-1 block text-sm font-medium text-gray-700">{t('cases.priority')}</label>
            <select {...register('priority')} className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary-500">
              <option value="Low">{t('cases.priorities.low')}</option>
              <option value="Medium">{t('cases.priorities.medium')}</option>
              <option value="High">{t('cases.priorities.high')}</option>
              <option value="Critical">{t('cases.priorities.critical')}</option>
            </select>
          </div>
          <div>
            <label className="mb-1 block text-sm font-medium text-gray-700">{t('cases.description')}</label>
            <textarea
              {...register('description')}
              rows={3}
              className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
            />
          </div>
        </form>
      </Modal>

      <ConfirmModal
        open={deleteId !== null}
        onClose={() => { setDeleteId(null); }}
        onConfirm={() => {
          if (deleteId) deleteCase.mutate(deleteId, { onSuccess: () => { setDeleteId(null); } });
        }}
        title={t('cases.deleteCase')}
        message={t('cases.deleteConfirm')}
        confirmLabel={t('common.delete')}
        loading={deleteCase.isPending}
      />
    </div>
  );
}

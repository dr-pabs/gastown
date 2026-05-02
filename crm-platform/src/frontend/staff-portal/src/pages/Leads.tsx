import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { zodResolver } from '@hookform/resolvers/zod';
import { useLeads, useCreateLead, useDeleteLead } from '../hooks/useLeads';
import { Table, Button, Input, Modal, ConfirmModal, Badge } from '../components/ui';
import type { Column } from '../components/ui';
import type { Lead, CreateLeadRequest, PaginationParams } from '../types';

const createLeadSchema = z.object({
  firstName: z.string().min(1),
  lastName: z.string().min(1),
  email: z.string().email(),
  phone: z.string().optional(),
  company: z.string().optional(),
  jobTitle: z.string().optional(),
  source: z.string().optional(),
});

type CreateLeadFormValues = z.infer<typeof createLeadSchema>;

export function Leads() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [params, setParams] = useState<PaginationParams>({ page: 1, pageSize: 20 });
  const [createOpen, setCreateOpen] = useState(false);
  const [deleteId, setDeleteId] = useState<string | null>(null);

  const { data, isLoading } = useLeads(params);
  const createLead = useCreateLead();
  const deleteLead = useDeleteLead();

  const { register, handleSubmit, reset, formState: { errors } } = useForm<CreateLeadFormValues>({
    resolver: zodResolver(createLeadSchema),
  });

  const onCreateSubmit = (values: CreateLeadFormValues) => {
    const req: CreateLeadRequest = values;
    createLead.mutate(req, {
      onSuccess: () => {
        setCreateOpen(false);
        reset();
      },
    });
  };

  const columns: Column<Lead>[] = [
    {
      key: 'firstName',
      header: t('leads.firstName'),
      render: (row) => `${row.firstName} ${row.lastName}`,
    },
    { key: 'email', header: t('common.email') },
    { key: 'company', header: t('leads.company') },
    {
      key: 'stage',
      header: t('leads.stage'),
      render: (row) => <Badge label={row.stage} variant="info" />,
    },
    {
      key: 'score',
      header: t('leads.score'),
      render: (row) => row.score !== undefined ? String(row.score) : '—',
    },
    {
      key: 'actions',
      header: t('common.actions'),
      render: (row) => (
        <button
          onClick={(e) => { e.stopPropagation(); setDeleteId(row.id); }}
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
        <h1 className="text-2xl font-bold text-gray-900">{t('leads.title')}</h1>
        <Button onClick={() => { setCreateOpen(true); }}>
          {t('leads.newLead')}
        </Button>
      </div>

      <Table
        columns={columns}
        data={data?.items ?? []}
        loading={isLoading}
        emptyMessage={t('common.noResults')}
        onRowClick={(row) => { void navigate(`/leads/${row.id}`); }}
      />

      {/* Pagination */}
      {data && data.totalPages > 1 && (
        <div className="mt-4 flex items-center justify-between text-sm text-gray-500">
          <span>
            {t('pagination.showing', {
              from: (params.page - 1) * params.pageSize + 1,
              to: Math.min(params.page * params.pageSize, data.totalCount),
              total: data.totalCount,
            })}
          </span>
          <div className="flex gap-2">
            <Button
              size="sm"
              variant="secondary"
              disabled={params.page <= 1}
              onClick={() => { setParams((p) => ({ ...p, page: p.page - 1 })); }}
            >
              {t('common.previous')}
            </Button>
            <Button
              size="sm"
              variant="secondary"
              disabled={params.page >= data.totalPages}
              onClick={() => { setParams((p) => ({ ...p, page: p.page + 1 })); }}
            >
              {t('common.next')}
            </Button>
          </div>
        </div>
      )}

      {/* Create modal */}
      <Modal
        open={createOpen}
        onClose={() => { setCreateOpen(false); reset(); }}
        title={t('leads.newLead')}
        footer={
          <>
            <Button variant="secondary" onClick={() => { setCreateOpen(false); reset(); }}>
              {t('common.cancel')}
            </Button>
            <Button
              form="create-lead-form"
              type="submit"
              loading={createLead.isPending}
            >
              {t('common.create')}
            </Button>
          </>
        }
      >
        <form
          id="create-lead-form"
          onSubmit={(e) => { void handleSubmit(onCreateSubmit)(e); }}
          className="space-y-4"
        >
          <div className="grid grid-cols-2 gap-4">
            <Input
              label={t('leads.firstName')}
              required
              {...register('firstName')}
              error={errors.firstName?.message}
            />
            <Input
              label={t('leads.lastName')}
              required
              {...register('lastName')}
              error={errors.lastName?.message}
            />
          </div>
          <Input
            label={t('common.email')}
            type="email"
            required
            {...register('email')}
            error={errors.email?.message}
          />
          <Input label={t('common.phone')} {...register('phone')} />
          <Input label={t('leads.company')} {...register('company')} />
          <Input label={t('leads.jobTitle')} {...register('jobTitle')} />
        </form>
      </Modal>

      {/* Delete confirm */}
      <ConfirmModal
        open={deleteId !== null}
        onClose={() => { setDeleteId(null); }}
        onConfirm={() => {
          if (deleteId) {
            deleteLead.mutate(deleteId, { onSuccess: () => { setDeleteId(null); } });
          }
        }}
        title={t('leads.deleteLead')}
        message={t('leads.deleteConfirm')}
        confirmLabel={t('common.delete')}
        loading={deleteLead.isPending}
      />
    </div>
  );
}

import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { zodResolver } from '@hookform/resolvers/zod';
import { useAccounts, useCreateAccount, useDeleteAccount } from '../hooks/useAccounts';
import { Table, Button, Input, Modal, ConfirmModal } from '../components/ui';
import type { Column } from '../components/ui';
import type { Account, CreateAccountRequest, PaginationParams } from '../types';

const createAccountSchema = z.object({
  name: z.string().min(1),
  industry: z.string().optional(),
  website: z.string().url().optional().or(z.literal('')),
  phone: z.string().optional(),
  employees: z.number().optional(),
});

type CreateAccountFormValues = z.infer<typeof createAccountSchema>;

export function Accounts() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [params, setParams] = useState<PaginationParams>({ page: 1, pageSize: 20 });
  const [createOpen, setCreateOpen] = useState(false);
  const [deleteId, setDeleteId] = useState<string | null>(null);

  const { data, isLoading } = useAccounts(params);
  const createAccount = useCreateAccount();
  const deleteAccount = useDeleteAccount();

  const { register, handleSubmit, reset, formState: { errors } } = useForm<CreateAccountFormValues>({
    resolver: zodResolver(createAccountSchema),
  });

  const onCreateSubmit = (values: CreateAccountFormValues) => {
    const req: CreateAccountRequest = {
      name: values.name,
      industry: values.industry,
      website: values.website || undefined,
      phone: values.phone,
      employees: values.employees,
    };
    createAccount.mutate(req, {
      onSuccess: () => { setCreateOpen(false); reset(); },
    });
  };

  const columns: Column<Account>[] = [
    { key: 'name', header: t('accounts.name') },
    { key: 'industry', header: t('accounts.industry') },
    { key: 'website', header: t('accounts.website') },
    {
      key: 'employees',
      header: t('accounts.employees'),
      render: (row) => row.employees?.toLocaleString() ?? '—',
    },
    {
      key: 'actions',
      header: t('common.actions'),
      render: (row) => (
        <button
          onClick={(e: React.MouseEvent) => { e.stopPropagation(); setDeleteId(row.id); }}
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
        <h1 className="text-2xl font-bold text-gray-900">{t('accounts.title')}</h1>
        <Button onClick={() => { setCreateOpen(true); }}>{t('accounts.newAccount')}</Button>
      </div>

      <Table
        columns={columns}
        data={data?.items ?? []}
        loading={isLoading}
        emptyMessage={t('common.noResults')}
        onRowClick={(row) => { void navigate(`/accounts/${row.id}`); }}
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
        title={t('accounts.newAccount')}
        footer={
          <>
            <Button variant="secondary" onClick={() => { setCreateOpen(false); reset(); }}>
              {t('common.cancel')}
            </Button>
            <Button form="create-account-form" type="submit" loading={createAccount.isPending}>
              {t('common.create')}
            </Button>
          </>
        }
      >
        <form
          id="create-account-form"
          onSubmit={(e) => { void handleSubmit(onCreateSubmit)(e); }}
          className="space-y-4"
        >
          <Input label={t('accounts.name')} required {...register('name')} error={errors.name?.message} />
          <Input label={t('accounts.industry')} {...register('industry')} />
          <Input label={t('accounts.website')} type="url" {...register('website')} error={errors.website?.message} />
          <Input label={t('common.phone')} {...register('phone')} />
        </form>
      </Modal>

      <ConfirmModal
        open={deleteId !== null}
        onClose={() => { setDeleteId(null); }}
        onConfirm={() => {
          if (deleteId) deleteAccount.mutate(deleteId, { onSuccess: () => { setDeleteId(null); } });
        }}
        title={t('accounts.deleteAccount')}
        message={t('accounts.deleteConfirm')}
        confirmLabel={t('common.delete')}
        loading={deleteAccount.isPending}
      />
    </div>
  );
}

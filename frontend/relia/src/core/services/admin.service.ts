import { Injectable, computed, inject, signal } from '@angular/core';

import { Operation } from '../../shared/types/agent';
import { AgentService } from './agent.service';

export interface Totals {
  total: number;
  count: number;
}

export interface AgentSummary {
  id: string;
  name: string;
  /** Responsable del punto */
  owner: string;
  city: string;
  /** Payment pointer Interledger al que se pagan las comisiones */
  pointer: string;
  /** Depósitos y retiros completados */
  operations: number;
  deposits: Totals;
  withdrawals: Totals;
  totalMoved: number;
  cupo: number;
  cash: number;
  commissionDue: number;
}

export interface CommissionPayment {
  /** PG-XXX */
  id: string;
  agentId: string;
  agentName: string;
  pointer: string;
  amount: number;
  paidAt: string;
}

const LIVE_AGENT = {
  id: 'dona-rosa',
  name: 'Tienda Doña Rosa',
  owner: 'Rosa Gómez',
  city: 'Bogotá',
  pointer: '$wallet.relia.co/dona-rosa',
};

// shortcut: red demo fija para el prototipo; cambiar por GET /api/admin/agents cuando exista backend
const DEMO_AGENTS: AgentSummary[] = [
  {
    id: 'la-esquina',
    name: 'Droguería La Esquina',
    owner: 'Carlos Pérez',
    city: 'Cali',
    pointer: '$wallet.relia.co/la-esquina',
    operations: 18,
    deposits: { total: 1_240_000, count: 11 },
    withdrawals: { total: 860_000, count: 7 },
    totalMoved: 2_100_000,
    cupo: 420_000,
    cash: 610_000,
    commissionDue: 9_000,
  },
  {
    id: 'el-sol',
    name: 'Minimercado El Sol',
    owner: 'Luz Marina Ríos',
    city: 'Medellín',
    pointer: '$wallet.relia.co/el-sol',
    operations: 9,
    deposits: { total: 530_000, count: 5 },
    withdrawals: { total: 390_000, count: 4 },
    totalMoved: 920_000,
    cupo: 150_000,
    cash: 280_000,
    commissionDue: 4_500,
  },
  {
    id: 'papeleria-central',
    name: 'Papelería Central',
    owner: 'Jorge Díaz',
    city: 'Barranquilla',
    pointer: '$wallet.relia.co/papeleria-central',
    operations: 0,
    deposits: { total: 0, count: 0 },
    withdrawals: { total: 0, count: 0 },
    totalMoved: 0,
    cupo: 80_000,
    cash: 120_000,
    commissionDue: 0,
  },
];

const sum = (ops: Operation[]): Totals => ({
  total: ops.reduce((acc, op) => acc + op.amount, 0),
  count: ops.length,
});

const isToday = (iso: string, now = new Date()): boolean =>
  new Date(iso).toDateString() === now.toDateString();

/** Visión global de la red para el admin (D1); Tienda Doña Rosa refleja en vivo AgentService */
@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly agent = inject(AgentService);
  /** Más recientes primero */
  private readonly _payments = signal<CommissionPayment[]>([]);
  readonly payments = this._payments.asReadonly();

  private readonly liveAgent = computed<AgentSummary>(() => {
    const done = this.agent.operations().filter((op) => op.status === 'completed');
    const deposits = sum(done.filter((op) => op.type === 'deposit'));
    const withdrawals = sum(done.filter((op) => op.type === 'withdrawal'));
    const { cupo, cash, commission } = this.agent.balances();
    return {
      ...LIVE_AGENT,
      operations: deposits.count + withdrawals.count,
      deposits,
      withdrawals,
      totalMoved: deposits.total + withdrawals.total,
      cupo,
      cash,
      commissionDue: commission,
    };
  });

  readonly agents = computed(() => [this.liveAgent(), ...DEMO_AGENTS]);

  readonly totals = computed(() => {
    const agents = this.agents();
    const add = (pick: (a: AgentSummary) => Totals): Totals =>
      agents.reduce(
        (acc, a) => ({ total: acc.total + pick(a).total, count: acc.count + pick(a).count }),
        { total: 0, count: 0 },
      );
    const deposits = add((a) => a.deposits);
    const withdrawals = add((a) => a.withdrawals);
    return {
      moved: {
        total: deposits.total + withdrawals.total,
        count: deposits.count + withdrawals.count,
      },
      deposits,
      withdrawals,
      commissionDue: agents.reduce((acc, a) => acc + a.commissionDue, 0),
      paidToday: this._payments()
        .filter((p) => isToday(p.paidAt))
        .reduce((acc, p) => acc + p.amount, 0),
    };
  });
}

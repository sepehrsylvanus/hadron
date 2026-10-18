import { Injectable, OnModuleDestroy } from '@nestjs/common';
import { PrismaPg } from '@prisma/adapter-pg';
import { PrismaClient } from '../generated/prisma/client';
@Injectable()
export class PrismaService extends PrismaClient implements OnModuleDestroy {
  constructor() {
    const url = new URL(process.env.DATABASE_URL ?? '');
    if (!['postgres:', 'postgresql:'].includes(url.protocol) ||
        !['127.0.0.1', 'localhost'].includes(url.hostname) ||
        !((url.port === '5544' && url.pathname === '/hadron_nx_lab') ||
          (url.port === '5545' && url.pathname === '/hadron_nx_test')) || url.search !== '') {
      throw new Error('Only the isolated lab/test database is allowed');
    }
    super({ adapter: new PrismaPg({ connectionString: url.toString(), connectionTimeoutMillis: 3000 }) });
  }
  async onModuleDestroy() { await this.$disconnect(); }
}

import { CanActivate, ExecutionContext, Injectable, SetMetadata, UnauthorizedException } from '@nestjs/common';
import { Reflector } from '@nestjs/core';
import { timingSafeEqual } from 'node:crypto';
export const PublicHealth = () => SetMetadata('publicHealth', true);
// Development service token only. This is NOT user login, RBAC or production authentication.
@Injectable()
export class LabGuard implements CanActivate {
  constructor(private readonly reflector: Reflector) {}
  canActivate(context: ExecutionContext): boolean {
    if (this.reflector.getAllAndOverride<boolean>('publicHealth', [context.getHandler(),context.getClass()])) return true;
    if (process.env.NODE_ENV === 'production') throw new UnauthorizedException('Lab credentials disabled in production');
    const expected = process.env.LAB_API_TOKEN ?? '';
    const value = context.switchToHttp().getRequest().headers.authorization;
    const supplied = typeof value === 'string' && value.startsWith('Bearer ') ? value.slice(7) : '';
    const a = Buffer.from(expected), b = Buffer.from(supplied);
    if (a.length < 64 || a.length !== b.length || !timingSafeEqual(a,b)) throw new UnauthorizedException();
    return true;
  }
}

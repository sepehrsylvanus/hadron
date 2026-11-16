import Decimal from 'decimal.js';
const D=Decimal.clone({precision:80,rounding:Decimal.ROUND_HALF_UP});
export class DomainError extends Error { constructor(readonly code:string){super(code);} }
export function positiveAmount(value:string):string {
  if(!/^(?:0|[1-9]\d{0,17})(?:\.\d{1,6})?$/.test(value)||!new D(value).gt(0)) throw new DomainError('INVALID_AMOUNT');
  return new D(value).toFixed();
}
export function assertVersion(expected:string,actual:string){
  if(!/^[1-9]\d*$/.test(expected)||expected!==actual) throw new DomainError('VERSION_CONFLICT');
}
export type Bom=Record<string,{baseOutputQty:string,lines:{childId:string,usageQty:string,scrapPercent:string}[]}>;
// Lab convention: scrap is an additive consumption allowance, not a yield-loss divisor.
export function explodeBom(root:string,quantity:string,graph:Bom):Record<string,string>{
  if(!new D(quantity).isFinite()||!new D(quantity).gt(0)) throw new DomainError('INVALID_QUANTITY');
  const totals=new Map<string,Decimal>();let visits=0;
  function visit(id:string,qty:Decimal,path:Set<string>){
    if(path.has(id)) throw new DomainError('BOM_CYCLE');
    if(++visits>10000||path.size>=64) throw new DomainError('BOM_TOO_COMPLEX');
    const node=graph[id];if(!node)return;
    const base=new D(node.baseOutputQty);
    if(!base.isFinite()||!base.gt(0)) throw new DomainError('INVALID_BASE_OUTPUT');
    const next=new Set(path);next.add(id);
    for(const line of node.lines){
      const usage=new D(line.usageQty),scrap=new D(line.scrapPercent);
      if(!usage.isFinite()||!usage.gt(0)||!scrap.isFinite()||scrap.lt(0)||scrap.gt(100)) throw new DomainError('INVALID_BOM_LINE');
      const required=qty.div(base).mul(usage).mul(new D(1).plus(scrap.div(100)));
      totals.set(line.childId,(totals.get(line.childId)??new D(0)).plus(required));
      visit(line.childId,required,next);
    }
  }
  visit(root,new D(quantity),new Set());
  return Object.fromEntries([...totals].sort(([a],[b])=>a.localeCompare(b)).map(([id,qty])=>[id,qty.toFixed()]));
}
export function netMaterial(gross:string,onHand:string,firmReceipts:string,safety:string){
  const values=[gross,onHand,firmReceipts,safety].map(x=>new D(x));
  if(values.some(x=>!x.isFinite()||x.lt(0)))throw new DomainError('INVALID_MRP_INPUT');
  const [g,h,r,s]=values;return D.max(0,g.plus(s).minus(h).minus(r)).toFixed();
}
export function assertOrderEditable(projectStatus:string,orderStatus:string,hasLinkedFx:boolean){
  if(['CLOSED','CANCELLED'].includes(projectStatus))throw new DomainError('PROJECT_CLOSED');
  if(orderStatus!=='DRAFT')throw new DomainError('ORDER_NOT_DRAFT');
  if(hasLinkedFx)throw new DomainError('ORDER_HAS_LINKED_FX');
}
export function assertOrderTransition(from:string,to:string,reason:string,correction=false){
  if(!reason.trim())throw new DomainError('REASON_REQUIRED');
  const normal:Record<string,string[]>={DRAFT:['SUBMITTED','CANCELLED'],SUBMITTED:['ACTIVE','SUSPENDED','CANCELLED'],ACTIVE:['SUSPENDED','CANCELLED'],SUSPENDED:['ACTIVE','CANCELLED'],CANCELLED:[]};
  const backward:Record<string,string[]>={SUBMITTED:['DRAFT'],ACTIVE:['SUBMITTED'],SUSPENDED:['SUBMITTED']};
  if(!normal[from]?.includes(to)&&!(correction&&backward[from]?.includes(to)))throw new DomainError('INVALID_TRANSITION');
}

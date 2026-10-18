-- Append after Prisma-generated table DDL, after review.
CREATE UNIQUE INDEX "partial_engineering.bom_revisions_Parent_d4464187c6" ON "engineering"."bom_revisions" ("ParentItemId") WHERE "Status" = 'APPROVED';
CREATE UNIQUE INDEX "partial_procurement.sourcing_options_Ite_4749de5b8c" ON "procurement"."sourcing_options" ("ItemId") WHERE "IsActive" AND "IsPreferred";
CREATE UNIQUE INDEX "partial_procurement.supplier_contacts_Su_5caf909368" ON "procurement"."supplier_contacts" ("SupplierId") WHERE "IsActive" AND "IsPrimary";

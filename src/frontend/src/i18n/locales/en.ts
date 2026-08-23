const en = {
  productName: 'Hadron ERP System',
  language: {
    label: 'Language',
    selectAria: 'Select interface language',
    persian: 'فارسی',
    english: 'English',
  },
  shell: {
    home: 'Home',
    workspace: 'Workspace',
    systemDescription: 'Hadron Tavan integrated enterprise system',
    mrpContext: 'MRP — Material Requirements Planning',
    logoHomeAria: 'Return to Hadron ERP System Home',
  },
  navigation: {
    sidebarAria: 'MRP workspace navigation',
    overview: 'Workspace Overview',
    expand: 'Expand MRP navigation',
    collapse: 'Collapse MRP navigation',
    groups: {
      masterData: 'Master Data',
      suppliersAndSourcing: 'Suppliers & Sourcing',
      engineering: 'Engineering',
    },
    items: {
      products: 'Products',
      parts: 'Parts',
      components: 'Components',
      referenceData: 'Reference Data',
      suppliers: 'Suppliers',
      sourcing: 'Sourcing',
      bom: 'BOM',
    },
  },
  breadcrumbs: {
    aria: 'Breadcrumb',
  },
  home: {
    eyebrow: 'Enterprise Resource Planning',
    description: 'A unified entry point to Hadron Tavan enterprise workspaces',
    workspacesEyebrow: 'Workspaces',
    workspaces: 'Workspaces',
    activeWorkspaceCount: '1 active workspace',
    mrpTitle: 'Material Requirements Planning',
    mrpDescription: 'Manage master data, sourcing, and BOM engineering structures',
    enterWorkspace: 'Enter workspace',
  },
  mrpWorkspace: {
    eyebrow: 'Material Requirements Planning',
    title: 'MRP Workspace',
    description: 'Access master data, suppliers, sourcing, and BOM engineering',
  },
} as const

export default en

const fa = {
  productName: 'Hadron ERP System',
  language: {
    label: 'زبان',
    selectAria: 'انتخاب زبان رابط کاربری',
    persian: 'فارسی',
    english: 'English',
  },
  shell: {
    home: 'خانه',
    workspace: 'فضای کاری',
    systemDescription: 'سامانه یکپارچه سازمانی هادرون توان',
    mrpContext: 'MRP — برنامه‌ریزی نیازمندی مواد',
    logoHomeAria: 'بازگشت به خانه Hadron ERP System',
  },
  navigation: {
    sidebarAria: 'ناوبری فضای کاری MRP',
    overview: 'نمای کلی فضای کاری',
    expand: 'بازکردن منوی MRP',
    collapse: 'جمع‌کردن منوی MRP',
    groups: {
      masterData: 'اطلاعات پایه',
      suppliersAndSourcing: 'تأمین‌کنندگان و منابع تأمین',
      engineering: 'مهندسی',
    },
    items: {
      products: 'محصولات',
      parts: 'قطعات',
      components: 'اجزا',
      referenceData: 'اطلاعات مرجع',
      suppliers: 'تأمین‌کنندگان',
      sourcing: 'منابع تأمین',
      bom: 'BOM',
    },
  },
  breadcrumbs: {
    aria: 'مسیر صفحه',
  },
  home: {
    eyebrow: 'Enterprise Resource Planning',
    description: 'نقطه ورود یکپارچه به فضاهای کاری سازمانی هادرون توان',
    workspacesEyebrow: 'Workspaces',
    workspaces: 'فضاهای کاری',
    activeWorkspaceCount: '۱ فضای کاری فعال',
    mrpTitle: 'برنامه‌ریزی نیازمندی مواد',
    mrpDescription: 'مدیریت اطلاعات پایه، منابع تأمین و ساختار مهندسی BOM',
    enterWorkspace: 'ورود به فضای کاری',
  },
  mrpWorkspace: {
    eyebrow: 'Material Requirements Planning',
    title: 'فضای کاری MRP',
    description: 'دسترسی به اطلاعات پایه، تأمین‌کنندگان، منابع تأمین و مهندسی BOM',
  },
} as const

export default fa

import { Routes } from '@angular/router';

/** One route per tab. Pages load on demand so the wall screen only pays for Hoje. */
export const routes: Routes = [
  { path: '', title: 'Hoje · Painel da Casa', loadComponent: () => import('./pages/home-page').then((m) => m.HomePage) },
  { path: 'fotos', title: 'Fotos · Painel da Casa', loadComponent: () => import('./pages/photos-page').then((m) => m.PhotosPage) },
  { path: 'viagens', title: 'Viagens · Painel da Casa', loadComponent: () => import('./pages/trips-page').then((m) => m.TripsPage) },
  { path: 'leituras', title: 'Leituras · Painel da Casa', loadComponent: () => import('./pages/books-page').then((m) => m.BooksPage) },
  { path: 'compras', title: 'Compras · Painel da Casa', loadComponent: () => import('./pages/shopping-page').then((m) => m.ShoppingPage) },
  { path: '**', redirectTo: '' },
];

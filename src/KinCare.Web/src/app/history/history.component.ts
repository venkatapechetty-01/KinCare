import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { HttpClient } from '@angular/common/http';
import { Subscription } from 'rxjs';
import { environment } from '../../environments/environment';

export interface HistoryRideDto {
  id: string;
  residentName: string;
  vendorName: string | null;
  status: string;
  dispatchChannel: string;
  pickupTime: string;
  pickupAddress: string;
  destinationAddress: string;
  facilityName: string;
}

export interface HistoryResponse {
  rides: HistoryRideDto[];
  total: number;
  page: number;
  pageSize: number;
}

@Component({
  selector: 'app-history',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatTableModule,
    MatPaginatorModule,
    MatFormFieldModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatDatepickerModule,
    MatNativeDateModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
  ],
  templateUrl: './history.component.html',
  styleUrl: './history.component.scss',
})
export class HistoryComponent implements OnInit, OnDestroy {
  filterForm: FormGroup;
  rides: HistoryRideDto[] = [];
  total = 0;
  page = 1;
  pageSize = 25;
  loading = false;
  exporting = false;

  displayedColumns: string[] = [
    'pickupTime',
    'residentName',
    'vendorName',
    'status',
    'dispatchChannel',
    'pickupAddress',
    'destinationAddress',
  ];

  statuses = ['Dispatched', 'Confirmed', 'EnRoute', 'Arrived', 'PickedUp', 'AtDestination', 'Dropped', 'Completed', 'Cancelled'];
  channels = ['SmsNemt', 'SmsTaxi', 'Broker'];

  private readonly apiUrl = `${environment.apiUrl}/api/rides/history`;
  private subscriptions = new Subscription();

  constructor(
    private fb: FormBuilder,
    private http: HttpClient,
    private router: Router,
    private snackBar: MatSnackBar
  ) {
    this.filterForm = this.fb.group({
      status: [''],
      channel: [''],
      from: [null],
      to: [null],
    });
  }

  ngOnInit(): void {
    this.loadHistory();

    // Reload when filters change
    this.subscriptions.add(
      this.filterForm.valueChanges.subscribe(() => {
        this.page = 1; // Reset to first page on filter change
        this.loadHistory();
      })
    );
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  loadHistory(): void {
    this.loading = true;
    const filters = this.filterForm.value;

    let params: any = {
      page: this.page,
      pageSize: this.pageSize,
    };

    if (filters.status) params.status = filters.status;
    if (filters.channel) params.channel = filters.channel;
    if (filters.from) params.from = new Date(filters.from).toISOString();
    if (filters.to) params.to = new Date(filters.to).toISOString();

    this.subscriptions.add(
      this.http.get<HistoryResponse>(this.apiUrl, { params }).subscribe({
        next: (response) => {
          this.rides = response.rides;
          this.total = response.total;
          this.loading = false;
        },
        error: (error) => {
          console.error('Error loading history:', error);
          this.snackBar.open('Failed to load ride history. Please try again.', 'Close', {
            duration: 5000,
            panelClass: ['error-snackbar'],
          });
          this.loading = false;
        },
      })
    );
  }

  onPageChange(event: PageEvent): void {
    this.page = event.pageIndex + 1;
    this.pageSize = event.pageSize;
    this.loadHistory();
  }

  clearFilters(): void {
    this.filterForm.reset({
      status: '',
      channel: '',
      from: null,
      to: null,
    });
  }

  exportCsv(): void {
    this.exporting = true;
    const filters = this.filterForm.value;

    let params: any = {};
    if (filters.from) params.from = new Date(filters.from).toISOString();
    if (filters.to) params.to = new Date(filters.to).toISOString();

    this.subscriptions.add(
      this.http
        .get(`${this.apiUrl}/export`, {
          params,
          responseType: 'blob',
        })
        .subscribe({
          next: (blob) => {
            const url = window.URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;
            link.download = `ride-history-${new Date().toISOString().split('T')[0]}.csv`;
            link.click();
            window.URL.revokeObjectURL(url);
            this.exporting = false;
            this.snackBar.open('Export completed!', 'Close', {
              duration: 3000,
              panelClass: ['success-snackbar'],
            });
          },
          error: (error) => {
            console.error('Error exporting CSV:', error);
            const errorMessage =
              error.status === 402
                ? 'CSV export requires a Professional plan.'
                : 'Failed to export CSV. Please try again.';
            this.snackBar.open(errorMessage, 'Close', {
              duration: 5000,
              panelClass: ['error-snackbar'],
            });
            this.exporting = false;
          },
        })
    );
  }

  viewRideDetail(id: string): void {
    this.router.navigate(['/rides', id]);
  }

  getStatusClass(status: string): string {
    const statusMap: Record<string, string> = {
      Dispatched:    'status-dispatched',
      Confirmed:     'status-confirmed',
      EnRoute:       'status-enroute',
      Arrived:       'status-arrived',
      PickedUp:      'status-pickedup',
      AtDestination: 'status-atdestination',
      Dropped:       'status-dropped',
      Completed:     'status-completed',
      Cancelled:     'status-cancelled',
    };
    return statusMap[status] || '';
  }

  getChannelIcon(channel: string): string {
    const iconMap: Record<string, string> = {
      SmsNemt: 'accessible',
      SmsTaxi: 'local_taxi',
      Broker: 'business',
    };
    return iconMap[channel] || 'help';
  }

  formatDateTime(dateStr: string): string {
    const date = new Date(dateStr);
    return date.toLocaleString('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
      hour12: true,
    });
  }
}

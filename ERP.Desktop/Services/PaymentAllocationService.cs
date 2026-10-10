using ERP.Desktop.Models;

namespace ERP.Desktop.Services;

public class PaymentAllocationService
{
    /// <summary>
    /// Ödeme Dağıtım Motoru:
    /// Öncelik Sırası:
    /// 1. Faiz (Interest)
    /// 2. Vergiler (BSMV + KKDF)
    /// 3. Masraflar (Fees/Expenses)
    /// 4. Anapara (Principal)
    /// 
    /// Kısmi/Asgari ödemede anapara kapanmaz, faiz ve vergi karşılandıktan sonra kalan anaparadan düşer.
    /// Kalan anapara üzerinden sonraki dönemlerde faiz işlemeye devam eder.
    /// </summary>
    public static PaymentBreakdown Allocate(
        decimal totalPaid,
        decimal accruedInterest,
        decimal accruedTax,
        decimal currentPrincipal,
        decimal accruedExpense = 0m)
    {
        var result = new PaymentBreakdown
        {
            TotalPaid = totalPaid
        };

        decimal remainingToAllocate = totalPaid;

        // 1. FAİZ DAĞITIMI
        if (remainingToAllocate > 0 && accruedInterest > 0)
        {
            decimal toInt = Math.Min(remainingToAllocate, accruedInterest);
            result.ToInterest = toInt;
            remainingToAllocate -= toInt;
            result.RemainingInterest = accruedInterest - toInt;
        }
        else
        {
            result.RemainingInterest = accruedInterest;
        }

        // 2. VERGİ DAĞITIMI (KKDF + BSMV)
        if (remainingToAllocate > 0 && accruedTax > 0)
        {
            decimal toTax = Math.Min(remainingToAllocate, accruedTax);
            result.ToTax = toTax;
            remainingToAllocate -= toTax;
            result.RemainingTax = accruedTax - toTax;
        }
        else
        {
            result.RemainingTax = accruedTax;
        }

        // 3. MASRAF / ÜCRET DAĞITIMI
        if (remainingToAllocate > 0 && accruedExpense > 0)
        {
            decimal toExp = Math.Min(remainingToAllocate, accruedExpense);
            result.ToExpense = toExp;
            remainingToAllocate -= toExp;
        }

        // 4. ANAPARA DAĞITIMI
        if (remainingToAllocate > 0 && currentPrincipal > 0)
        {
            decimal toPrin = Math.Min(remainingToAllocate, currentPrincipal);
            result.ToPrincipal = toPrin;
            remainingToAllocate -= toPrin;
            result.RemainingPrincipal = currentPrincipal - toPrin;
        }
        else
        {
            result.RemainingPrincipal = currentPrincipal;
        }

        return result;
    }
}

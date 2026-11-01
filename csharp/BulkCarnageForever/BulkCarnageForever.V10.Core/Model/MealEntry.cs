using System;
using System.Collections.Generic;
using System.Text;

namespace BulkCarnageForever.V10.Core.Model
{
    public class MealEntry
    {
        public long Id { get; set; }

        public DateOnly Date { get; set; }
            = DateOnly.FromDateTime(DateTime.Today);

        public MealType MealType { get; set; }

        public string FoodName { get; set; } = string.Empty;

        public double AmountEaten { get; set; }
    }
}

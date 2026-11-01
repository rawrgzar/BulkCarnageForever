using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace BulkCarnageForever.V10.Core.Model
{
    public class FoodItem
    {
        [Key]
        public string RecipeName { get; set; } = string.Empty;

        public string? BrandType { get; set; }
        public string? GroupName { get; set; }

        public double Servings { get; set; } = 1;
        public double CaloriesPerServing { get; set; }
        public double MeasurementServings { get; set; }
        public string? MeasurementType { get; set; }

        public double Protein { get; set; }
        public double Carbs { get; set; }
        public double Fats { get; set; }
        public double Fiber { get; set; }

        public bool IsBreakfast { get; set; }
        public bool IsLunch { get; set; }
        public bool IsDinner { get; set; }
        public bool IsSnack { get; set; }

        public string? Link { get; set; }
        public string? PictureLink { get; set; }
    }
}

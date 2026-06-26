namespace WebApplication2.Models
{
    public class PromotionCustomer
    {
        public int PromotionId { get; set; }
        public Promotion Promotion { get; set; }

        public int CustomerId { get; set; }
        public Customer Customer { get; set; }

        public bool IsUsed { get; set; } = false;
    }
}
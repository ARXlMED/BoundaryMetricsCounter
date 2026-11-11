fn calculate_score(values: &[i32]) -> i32 {
    let mut score = 0;
    let mut index = 0usize;
    while index < values.len() {
        let value = values[index];
        if value != 0 {
            if value > 0 {
                score += value;
            } else {
                score -= value.abs();
            }
        } else {
            score += 1;
        }
        index += 1;
    }
    score
}
fn classify(value: i32) -> &'static str {
    match value {
        0 => "zero",
        1 => "one",
        2 => "two",
        3 => "three",
        _ => "other",
    }
}
fn process(values: &[i32]) -> i32 {
    let mut total = 0;
    let mut index = 0usize;
    while index < values.len() {
        let value = values[index];
        if value % 2 == 0 {
            let counter = 0usize;
            match counter {
                0 => total += 0,      
                1 => {                
                    if value > 10 {
                        total += 10;
                    } else {
                        total += 1;
                    }
                }
                _ => total += 0,      
            }
        }
        index += 1;
    }
    total
}
fn main() {
    let values = vec![0, 1, 2, 5, 10, 15];

    let score = calculate_score(&values);
    let result = process(&values);

    println!("Score: {}", score);
    println!("Result: {}", result);

    let mut index = 0usize;

    while index < values.len() {
        let value = &values[index];

        if *value >= 0 {
            if *value > 10 {
                println!("{} -> large", value);
            } else {
                println!("{} -> {}", value, classify(*value));
            }
        } else {
            println!("{} -> negative", value);
        }

        index += 1;
    }
    println!("Score calculated: {}", score);
}
